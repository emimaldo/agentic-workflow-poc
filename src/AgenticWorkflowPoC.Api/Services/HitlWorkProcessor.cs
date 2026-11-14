using System.Text.Json;
using AgenticWorkflowPoC.Core.Entities;
using AgenticWorkflowPoC.Core.Interfaces;
using AgenticWorkflowPoC.Plugins.Financial;

namespace AgenticWorkflowPoC.Api.Services
{
    public sealed class HitlWorkProcessor
    {
        private static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(2);
        private const int MaxAttempts = 3;

        private readonly IHitlSessionRepository _repository;
        private readonly FinancialTransferPlugin _transferPlugin;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<HitlWorkProcessor> _logger;

        public HitlWorkProcessor(
            IHitlSessionRepository repository,
            FinancialTransferPlugin transferPlugin,
            TimeProvider timeProvider,
            ILogger<HitlWorkProcessor> logger)
        {
            _repository = repository;
            _transferPlugin = transferPlugin;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default)
        {
            var session = await _repository.TryClaimNextApprovedAsync(_timeProvider.GetUtcNow(), ProcessingLease, cancellationToken);
            if (session is null)
            {
                return false;
            }

            if (session.ExecutionAttempts > MaxAttempts)
            {
                await TransitionAsync(session, HitlSessionStatus.Failed, cancellationToken);
                _logger.LogError("HITL session {SessionId} exhausted its processing attempts.", session.SessionId);
                return true;
            }

            EnrichedTransferCommand? command;
            try
            {
                command = JsonSerializer.Deserialize<EnrichedTransferCommand>(session.SerializedContext);
            }
            catch (JsonException exception)
            {
                _logger.LogError(exception, "HITL session {SessionId} has an invalid stored context.", session.SessionId);
                await TransitionAsync(session, HitlSessionStatus.Failed, cancellationToken);
                return true;
            }

            if (command is null)
            {
                _logger.LogError("HITL session {SessionId} has an empty stored context.", session.SessionId);
                await TransitionAsync(session, HitlSessionStatus.Failed, cancellationToken);
                return true;
            }

            // Defense in depth: the stored context must belong to the tenant that owns the session row.
            if (!string.Equals(command.TenantId, session.TenantId, StringComparison.Ordinal))
            {
                _logger.LogCritical(
                    "HITL session {SessionId} context does not match its tenant; refusing to execute.",
                    session.SessionId);
                await TransitionAsync(session, HitlSessionStatus.Failed, cancellationToken);
                return true;
            }

            try
            {
                // No LLM here: the immutable context is re-evaluated deterministically by the plugin.
                var outcome = await _transferPlugin.ExecuteApprovedTransferAsync(session.SessionId, command, cancellationToken);

                if (outcome.Kind == TransferOutcomeKind.Completed)
                {
                    await TransitionAsync(session, HitlSessionStatus.Completed, cancellationToken);
                    return true;
                }

                // Bounded Re-evaluation failed: the domain changed since approval. Terminal, nothing executed.
                var reason = $"{outcome.RejectionCode}: {outcome.Message}";
                if (!await _repository.TryInvalidateAsync(
                        session.TenantId,
                        session.SessionId,
                        reason.Length <= 200 ? reason : reason[..200],
                        cancellationToken))
                {
                    _logger.LogWarning("HITL session {SessionId} could not be invalidated.", session.SessionId);
                }

                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Attempt {Attempt}/{MaxAttempts} failed for approved HITL session {SessionId}.",
                    session.ExecutionAttempts,
                    MaxAttempts,
                    session.SessionId);

                if (session.ExecutionAttempts >= MaxAttempts)
                {
                    await TransitionAsync(session, HitlSessionStatus.Failed, cancellationToken);
                    return true;
                }

                // Backoff: leave the session in Processing. It is retried only after its lease expires
                // and the worker's sweep reclaims it, so a transient outage (failover, deadlock, network
                // blip) cannot burn every attempt in a tight loop. Execution is idempotent by operation id,
                // so a retry after an effect that did commit completes without a second debit.
                return true;
            }
        }

        private async Task TransitionAsync(
            HitlSession session,
            HitlSessionStatus nextStatus,
            CancellationToken cancellationToken)
        {
            var transitioned = await _repository.TryTransitionAsync(
                session.TenantId,
                session.SessionId,
                HitlSessionStatus.Processing,
                nextStatus,
                cancellationToken);
            if (!transitioned)
            {
                _logger.LogWarning(
                    "HITL session {SessionId} could not transition from Processing to {Status}.",
                    session.SessionId,
                    nextStatus);
            }
        }
    }
}
