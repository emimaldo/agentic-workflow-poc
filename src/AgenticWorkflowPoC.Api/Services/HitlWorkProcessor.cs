using System.Text.Json;
using AgenticWorkflowPoC.Core.Entities;
using AgenticWorkflowPoC.Core.Interfaces;
using AgenticWorkflowPoC.Plugins.Operations;

namespace AgenticWorkflowPoC.Api.Services
{
    public sealed class HitlWorkProcessor
    {
        private static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(2);
        private const int MaxAttempts = 3;

        private readonly IHitlSessionRepository _repository;
        private readonly StaffOverridesPlugin _staffOverridesPlugin;
        private readonly ILogger<HitlWorkProcessor> _logger;

        public HitlWorkProcessor(
            IHitlSessionRepository repository,
            StaffOverridesPlugin staffOverridesPlugin,
            ILogger<HitlWorkProcessor> logger)
        {
            _repository = repository;
            _staffOverridesPlugin = staffOverridesPlugin;
            _logger = logger;
        }

        public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            var session = await _repository.TryClaimNextApprovedAsync(now, ProcessingLease, cancellationToken);
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

            PendingStaffOverride? pendingAction;
            try
            {
                pendingAction = JsonSerializer.Deserialize<PendingStaffOverride>(session.PendingActionPayload);
            }
            catch (JsonException exception)
            {
                _logger.LogError(exception, "HITL session {SessionId} has an invalid stored action.", session.SessionId);
                await TransitionAsync(session, HitlSessionStatus.Failed, cancellationToken);
                return true;
            }

            if (pendingAction is null || string.IsNullOrWhiteSpace(pendingAction.StaffId))
            {
                _logger.LogError("HITL session {SessionId} has an empty stored action.", session.SessionId);
                await TransitionAsync(session, HitlSessionStatus.Failed, cancellationToken);
                return true;
            }

            try
            {
                var result = await _staffOverridesPlugin.ExecuteApprovedAvailabilityAsync(
                    session.SessionId,
                    session.TenantId,
                    pendingAction.StaffId,
                    pendingAction.NewAvailability,
                    cancellationToken);

                var finalStatus = result.Outcome == StaffOverrideOutcome.Completed
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
