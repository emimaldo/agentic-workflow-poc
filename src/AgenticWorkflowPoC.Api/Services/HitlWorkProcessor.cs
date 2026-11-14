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
                command = JsonSerializer.Deserialize<EnrichedTransferCommand>(session.PendingActionPayload);
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

            try
            {
                var outcome = await _transferPlugin.ExecuteApprovedTransferAsync(session.SessionId, command, cancellationToken);

                var finalStatus = outcome.Kind == TransferOutcomeKind.Completed
                    ? HitlSessionStatus.Completed
                    : HitlSessionStatus.Failed;
                await TransitionAsync(session, finalStatus, cancellationToken);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to process approved HITL session {SessionId}.", session.SessionId);
                var retryStatus = session.ExecutionAttempts >= MaxAttempts
                    ? HitlSessionStatus.Failed
                    : HitlSessionStatus.Approved;
                await TransitionAsync(session, retryStatus, cancellationToken);
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
