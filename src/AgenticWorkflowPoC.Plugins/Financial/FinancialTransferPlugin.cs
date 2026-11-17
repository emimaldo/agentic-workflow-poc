using System.ComponentModel;
using System.Globalization;
using AgenticWorkflowPoC.Core.Entities;
using AgenticWorkflowPoC.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace AgenticWorkflowPoC.Plugins.Financial
{
    public enum TransferOutcomeKind
    {
        Completed,
        RequiresApproval,
        Rejected
    }

    public sealed record TransferOutcome(
        TransferOutcomeKind Kind,
        string Message,
        TransferRejectionCode? RejectionCode = null);

    /// <summary>
    /// Native C# domain plugin (Chapters 2-3). It receives an already validated and tenant-enriched
    /// command; it never sees raw LLM output. Business-rule conflicts are expected domain forks:
    /// they mutate <see cref="IHitlState"/> or return a rejection instead of throwing.
    /// </summary>
    public sealed class FinancialTransferPlugin
    {
        private readonly ILogger<FinancialTransferPlugin> _logger;
        private readonly IHitlState _hitlState;
        private readonly ILedgerReader _ledgerReader;
        private readonly ITransferExecutor _executor;
        private readonly ITenantPolicyProvider _policyProvider;
        private readonly TimeProvider _timeProvider;

        public FinancialTransferPlugin(
            ILogger<FinancialTransferPlugin> logger,
            IHitlState hitlState,
            ILedgerReader ledgerReader,
            ITransferExecutor executor,
            ITenantPolicyProvider policyProvider,
            TimeProvider timeProvider)
        {
            _logger = logger;
            _hitlState = hitlState;
            _ledgerReader = ledgerReader;
            _executor = executor;
            _policyProvider = policyProvider;
            _timeProvider = timeProvider;
        }

        [Description("Executes a validated financial transfer or suspends it for human approval.")]
        public async Task<TransferOutcome> ExecuteTransferAsync(
            Guid operationId,
            EnrichedTransferCommand command,
            CancellationToken cancellationToken = default)
        {
            var policy = _policyProvider.GetRequiredPolicy(command.TenantId);
            var now = _timeProvider.GetUtcNow();
            var snapshot = await ReadSnapshotAsync(operationId, command, now, cancellationToken);

            // An operation that already executed is an idempotent replay: never re-judge it.
            if (!snapshot.OperationAlreadyExecuted)
            {
                var violation = TransferPolicy.EvaluateTenantLimits(command, policy)
                    ?? TransferPolicy.EvaluateLedger(command, snapshot, policy);
                if (violation is not null)
                {
                    return Reject(violation);
                }

                if (TransferPolicy.RequiresHumanApproval(command, policy))
                {
                    var reason = string.Format(
                        CultureInfo.InvariantCulture,
                        "Transfer of {0:N2} {1} exceeds the approval threshold of {2:N2}; manual sign-off required.",
                        command.Amount,
                        command.Currency,
                        policy.HitlThreshold);

                    // Out-of-band suspension: mutate request-scoped state instead of throwing.
                    _hitlState.Suspend(reason, command);
                    return new TransferOutcome(TransferOutcomeKind.RequiresApproval, "Transfer suspended pending human review.");
                }
            }

            return await ExecuteAsync(operationId, command, policy, now, cancellationToken);
        }

        /// <summary>
        /// Executes a transfer a human operator has approved, after a Bounded Re-evaluation:
        /// the approval is a hint, not an authorization. Hours may have passed since suspension, so the
        /// same deterministic rules run again against fresh ledger state and the current tenant policy.
        /// Bounded: one deterministic pass, no LLM call, and the approval threshold is not re-applied
        /// (that is exactly what the operator signed off). A failure here means "do not execute".
        /// </summary>
        public async Task<TransferOutcome> ExecuteApprovedTransferAsync(
            Guid operationId,
            EnrichedTransferCommand command,
            CancellationToken cancellationToken = default)
        {
            var policy = _policyProvider.GetRequiredPolicy(command.TenantId);
            var now = _timeProvider.GetUtcNow();
            var snapshot = await ReadSnapshotAsync(operationId, command, now, cancellationToken);

            if (!snapshot.OperationAlreadyExecuted)
            {
                var violation = TransferPolicy.EvaluateTenantLimits(command, policy)
                    ?? TransferPolicy.EvaluateLedger(command, snapshot, policy);
                if (violation is not null)
                {
                    _logger.LogWarning(
                        "Approved transfer {OperationId} failed re-evaluation: {Code}.",
                        operationId,
                        violation.Code);
                    return Reject(violation);
                }
            }

            // The executor re-checks balance, freeze and daily limit atomically (closes the TOCTOU window).
            return await ExecuteAsync(operationId, command, policy, now, cancellationToken);
        }

        /// <summary>
        /// True when the ledger already holds the transfer for this operation id. A HITL session's
        /// operation id is its session id, which the router never lets another transfer reuse.
        /// </summary>
        public async Task<bool> HasExecutedAsync(
            Guid operationId,
            EnrichedTransferCommand command,
            CancellationToken cancellationToken = default)
        {
            var snapshot = await ReadSnapshotAsync(operationId, command, _timeProvider.GetUtcNow(), cancellationToken);
            return snapshot.OperationAlreadyExecuted;
        }

        private Task<LedgerSnapshot> ReadSnapshotAsync(
            Guid operationId,
            EnrichedTransferCommand command,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            _ledgerReader.GetSnapshotAsync(
                new LedgerQuery(command.TenantId, command.SourceAccountId, command.DestinationAccount, operationId, now),
                cancellationToken);

        private async Task<TransferOutcome> ExecuteAsync(
            Guid operationId,
            EnrichedTransferCommand command,
            TenantPolicy policy,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            var status = await _executor.ExecuteAsync(
                new TransferExecutionRequest(operationId, command, policy.DailyLimit, now),
                cancellationToken);

            _logger.LogInformation(
                "Transfer {OperationId} to {Destination} finished with {Status}.",
                operationId,
                TransferPolicy.Mask(command.DestinationAccount),
                status);

            return status switch
            {
                TransferExecutionStatus.Executed or TransferExecutionStatus.AlreadyExecuted => new TransferOutcome(
                    TransferOutcomeKind.Completed,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Transfer of {0:N2} {1} to {2} executed.",
                        command.Amount,
                        command.Currency,
                        TransferPolicy.Mask(command.DestinationAccount))),
                TransferExecutionStatus.AccountNotFound => Reject(new(TransferRejectionCode.AccountNotFound, "The tenant operating account does not exist.")),
                TransferExecutionStatus.AccountFrozen => Reject(new(TransferRejectionCode.AccountFrozen, "The tenant operating account is frozen.")),
                TransferExecutionStatus.CurrencyMismatch => Reject(new(TransferRejectionCode.CurrencyMismatch, "The transfer currency does not match the operating account currency.")),
                TransferExecutionStatus.InsufficientFunds => Reject(new(TransferRejectionCode.InsufficientFunds, "The operating account has insufficient funds.")),
                TransferExecutionStatus.DailyLimitExceeded => Reject(new(TransferRejectionCode.DailyLimitExceeded, "The transfer would exceed the tenant daily limit.")),
                TransferExecutionStatus.IdempotencyConflict => Reject(new(TransferRejectionCode.OperationIdReused, "This session id was already used for a different transfer; start a new session.")),
                _ => throw new InvalidOperationException($"Unexpected execution status {status}.")
            };
        }

        private static TransferOutcome Reject(PolicyViolation violation) =>
            new(TransferOutcomeKind.Rejected, violation.Message, violation.Code);
    }
}
