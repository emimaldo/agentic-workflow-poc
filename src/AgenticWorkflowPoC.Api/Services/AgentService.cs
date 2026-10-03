using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Api.Models;
using AgenticWorkflowPoC.Core.Entities;
using AgenticWorkflowPoC.Core.Interfaces;
using AgenticWorkflowPoC.Plugins;
using AgenticWorkflowPoC.Plugins.Operations;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using AgentResponse = AgenticWorkflowPoC.Api.Models.AgentResponse;

namespace AgenticWorkflowPoC.Api.Services
{
    public class AgentService : IAgentService
    {
        private readonly AIAgent _extractionAgent;
        private readonly IHitlState _hitlState;
        private readonly ILogger<AgentService> _logger;
        private readonly StaffOverridesPlugin _staffOverridesPlugin;
        private readonly IExtractionValidator _validator;
        private readonly IHitlSessionRepository _hitlSessionRepository;

        public AgentService(
            AIAgent extractionAgent,
            IHitlState hitlState,
            ILogger<AgentService> logger,
            StaffOverridesPlugin staffOverridesPlugin,
            IExtractionValidator validator,
            IHitlSessionRepository hitlSessionRepository)
        {
            _extractionAgent = extractionAgent;
            _hitlState = hitlState;
            _logger = logger;
            _staffOverridesPlugin = staffOverridesPlugin;
            _validator = validator;
            _hitlSessionRepository = hitlSessionRepository;
        }

        public async Task<AgentResponse> InvokeAsync(Guid sessionId, string prompt, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("InvokeAgent called for SessionId={SessionId}", sessionId);

            var modelResponse = await _extractionAgent.RunAsync(
                prompt,
                cancellationToken: cancellationToken);
            var content = modelResponse.Text;
            _logger.LogDebug("Model returned content: {Content}", content ?? "<null>");

            if (string.IsNullOrWhiteSpace(content))
            {
                return new AgentResponse("Error", "No usable JSON was returned by the model. Please provide the staff ID and date clearly.", sessionId.ToString());
            }

            var validation = _validator.Validate(content);
            if (!validation.IsValid)
            {
                return new AgentResponse("Error", validation.Error ?? "invalid_extraction", sessionId.ToString());
            }

            var staffId = validation.StaffId!;
            var date = validation.Date!.Value;

            _logger.LogInformation("Invoking StaffOverridesPlugin for staffId={StaffId} date={Date}", staffId, date);
            var pluginResult = await _staffOverridesPlugin.OverrideAvailabilityAsync(staffId, date, cancellationToken);

            if (pluginResult.Outcome == StaffOverrideOutcome.InvalidInput)
            {
                return new AgentResponse("Error", pluginResult.Message, sessionId.ToString());
            }

            if (pluginResult.Outcome == StaffOverrideOutcome.RequiresApproval && _hitlState.IsSuspended)
            {
                var pendingAction = new PendingStaffOverride(staffId, date);
                var pendingSession = new HitlSession(
                    sessionId,
                    _hitlState.Reason,
                    JsonSerializer.Serialize(pendingAction),
                    HitlSessionStatus.PendingReview,
                    DateTimeOffset.UtcNow);

                await _hitlSessionRepository.CreatePendingAsync(pendingSession, cancellationToken);
                _logger.LogWarning("Operation suspended for SessionId={SessionId}: {Reason}", sessionId, _hitlState.Reason);
                return new AgentResponse("Suspended", _hitlState.Reason, sessionId.ToString());
            }

            if (pluginResult.Outcome == StaffOverrideOutcome.RequiresApproval)
            {
                return new AgentResponse("Error", "HITL state was not set by the plugin.", sessionId.ToString());
            }

            _logger.LogInformation("Operation completed for SessionId={SessionId}", sessionId);
            return new AgentResponse("Completed", Response: pluginResult.Message, SessionId: sessionId.ToString());
        }

        public async Task<AgentResponse> ResumeAsync(Guid sessionId, bool isApproved, CancellationToken cancellationToken = default)
        {
            var pendingSession = await _hitlSessionRepository.GetAsync(sessionId, cancellationToken);
            if (pendingSession is null)
            {
                return new AgentResponse("NotFound", "HITL session was not found.", sessionId.ToString());
            }

            if (pendingSession.Status != HitlSessionStatus.PendingReview)
            {
                return new AgentResponse("Conflict", $"HITL session is already {pendingSession.Status}.", sessionId.ToString());
            }

            var resolvedStatus = isApproved ? HitlSessionStatus.Approved : HitlSessionStatus.Rejected;
            var transitioned = await _hitlSessionRepository.TryTransitionAsync(
                sessionId,
                HitlSessionStatus.PendingReview,
                resolvedStatus,
                cancellationToken);

            if (!transitioned)
            {
                return new AgentResponse("Conflict", "HITL session has already been resolved.", sessionId.ToString());
            }

            if (!isApproved)
            {
                return new AgentResponse("Rejected", "The pending operation was rejected.", sessionId.ToString());
            }

            PendingStaffOverride? pendingAction;
            try
            {
                pendingAction = JsonSerializer.Deserialize<PendingStaffOverride>(pendingSession.PendingActionPayload);
            }
            catch (JsonException)
            {
                await _hitlSessionRepository.TryTransitionAsync(
                    sessionId,
                    HitlSessionStatus.Approved,
                    HitlSessionStatus.Failed,
                    cancellationToken);
                return new AgentResponse("Error", "The stored pending action is invalid.", sessionId.ToString());
            }

            if (pendingAction is null)
            {
                await _hitlSessionRepository.TryTransitionAsync(
                    sessionId,
                    HitlSessionStatus.Approved,
                    HitlSessionStatus.Failed,
                    cancellationToken);
                return new AgentResponse("Error", "The stored pending action is empty.", sessionId.ToString());
            }

            try
            {
                var result = await _staffOverridesPlugin.ExecuteApprovedAvailabilityAsync(
                    pendingAction.StaffId,
                    pendingAction.NewAvailability,
                    cancellationToken);

                if (result.Outcome != StaffOverrideOutcome.Completed)
                {
                    await _hitlSessionRepository.TryTransitionAsync(
                        sessionId,
                        HitlSessionStatus.Approved,
                        HitlSessionStatus.Failed,
                        cancellationToken);
                    return new AgentResponse("Error", result.Message, sessionId.ToString());
                }

                await _hitlSessionRepository.TryTransitionAsync(
                    sessionId,
                    HitlSessionStatus.Approved,
                    HitlSessionStatus.Completed,
                    cancellationToken);

                return new AgentResponse("Completed", Response: result.Message, SessionId: sessionId.ToString());
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to execute approved HITL session {SessionId}", sessionId);
                await _hitlSessionRepository.TryTransitionAsync(
                    sessionId,
                    HitlSessionStatus.Approved,
                    HitlSessionStatus.Failed,
                    cancellationToken);
                return new AgentResponse("Error", "The approved operation failed.", sessionId.ToString());
            }
        }
    }
}
