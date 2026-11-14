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
using Microsoft.Extensions.AI;
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

        public async Task<AgentResponse> InvokeAsync(
            Guid sessionId,
            string tenantId,
            string prompt,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("InvokeAgent called for SessionId={SessionId}", sessionId);

            if (sessionId == Guid.Empty || string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(prompt))
            {
                return new AgentResponse("Error", "A session, tenant, and prompt are required.", sessionId.ToString());
            }

            var modelResponse = await _extractionAgent.RunAsync(
                prompt,
                options: new ChatClientAgentRunOptions
                {
                    ChatOptions = new ChatOptions
                    {
                        ResponseFormat = ChatResponseFormat.ForJsonSchema<StaffAvailabilityExtraction>(),
                        Temperature = 0,
                        Tools = Array.Empty<AITool>()
                    }
                },
                cancellationToken: cancellationToken);
            var content = modelResponse.Text;

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
            var pluginResult = await _staffOverridesPlugin.OverrideAvailabilityAsync(
                sessionId,
                tenantId,
                staffId,
                date,
                cancellationToken);

            if (pluginResult.Outcome == StaffOverrideOutcome.InvalidInput)
            {
                return new AgentResponse("Error", pluginResult.Message, sessionId.ToString());
            }

            if (pluginResult.Outcome == StaffOverrideOutcome.RequiresApproval && _hitlState.IsSuspended)
            {
                var pendingAction = new PendingStaffOverride(staffId, date);
                var pendingSession = new HitlSession(
                    sessionId,
                    tenantId,
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

        public async Task<AgentResponse> ResumeAsync(
            Guid sessionId,
            string tenantId,
            bool isApproved,
            CancellationToken cancellationToken = default)
        {
            var pendingSession = await _hitlSessionRepository.GetAsync(tenantId, sessionId, cancellationToken);
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
                tenantId,
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

            return new AgentResponse("Queued", "The approved operation was queued for background processing.", sessionId.ToString());
        }

    }
}
