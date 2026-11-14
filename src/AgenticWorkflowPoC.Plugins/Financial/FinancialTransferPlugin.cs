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

            var limitViolation = TransferPolicy.EvaluateTenantLimits(command, policy);
            if (limitViolation is not null)
            {
                return Reject(limitViolation);
            }

            var snapshot = await _ledgerReader.GetSnapshotAsync(
                new LedgerQuery(command.TenantId, command.SourceAccountId, command.DestinationAccount, operationId, now),
                cancellationToken);

            if (!snapshot.OperationAlreadyExecuted)
            {
                var ledgerViolation = TransferPolicy.EvaluateLedger(command, snapshot, policy);
                if (ledgerViolation is not null)
                {
                    return Reject(ledgerViolation);
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
                    _hitlState.Suspend(reason);
                    return new TransferOutcome(TransferOutcomeKind.RequiresApproval, "Transfer suspended pending human review.");
                }
            }

            return await ExecuteAsync(operationId, command, policy, now, cancellationToken);
        }

        /// <summary>
        /// Executes a transfer a human operator has approved. Never re-opens a review.
        /// </summary>
        public Task<TransferOutcome> ExecuteApprovedTransferAsync(
            Guid operationId,
            EnrichedTransferCommand command,
            CancellationToken cancellationToken = default)
        {
            var policy = _policyProvider.GetRequiredPolicy(command.TenantId);
            return ExecuteAsync(operationId, command, policy, _timeProvider.GetUtcNow(), cancellationToken);
        }

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
                _ => throw new InvalidOperationException($"Unexpected execution status {status}.")
            };
        }

        private static TransferOutcome Reject(PolicyViolation violation) =>
            new(TransferOutcomeKind.Rejected, violation.Message, violation.Code);
    }
}
