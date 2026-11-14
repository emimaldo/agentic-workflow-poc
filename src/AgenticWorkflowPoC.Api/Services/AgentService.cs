using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Api.Models;
using AgenticWorkflowPoC.Api.Security;
using AgenticWorkflowPoC.Core.Entities;
using AgenticWorkflowPoC.Core.Interfaces;
using AgenticWorkflowPoC.Plugins;
using AgenticWorkflowPoC.Plugins.Financial;
using Microsoft.Extensions.Logging;

namespace AgenticWorkflowPoC.Api.Services
{
    public class AgentService : IAgentService
    {
        private readonly IExtractionValidator<EnrichedTransferCommand> _validator;
        private readonly FinancialTransferPlugin _transferPlugin;
        private readonly IHitlState _hitlState;
        private readonly IHitlSessionRepository _hitlSessionRepository;
        private readonly ITenantAccessor _tenantAccessor;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<AgentService> _logger;

        public AgentService(
            IExtractionValidator<EnrichedTransferCommand> validator,
            FinancialTransferPlugin transferPlugin,
            IHitlState hitlState,
            IHitlSessionRepository hitlSessionRepository,
            ITenantAccessor tenantAccessor,
            TimeProvider timeProvider,
            ILogger<AgentService> logger)
        {
            _validator = validator;
            _transferPlugin = transferPlugin;
            _hitlState = hitlState;
            _hitlSessionRepository = hitlSessionRepository;
            _tenantAccessor = tenantAccessor;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        public async Task<AgentResponse> InvokeAsync(Guid sessionId, string prompt, CancellationToken cancellationToken = default)
        {
            if (sessionId == Guid.Empty || string.IsNullOrWhiteSpace(prompt))
            {
                return new AgentResponse("Error", "A session and prompt are required.", sessionId.ToString());
            }

            // 1. LLM as a pure parser, intercepted by the deterministic validator.
            var extraction = await _validator.ExtractAndValidateAsync(prompt, cancellationToken);
            if (!extraction.IsValid)
            {
                var message = string.Join(" ", extraction.Errors.Select(e => e.Message));
                return new AgentResponse("Error", message, sessionId.ToString());
            }

            var command = extraction.Data;

            // 2. C# owns execution and routing.
            var outcome = await _transferPlugin.ExecuteTransferAsync(sessionId, command, cancellationToken);

            switch (outcome.Kind)
            {
                case TransferOutcomeKind.Completed:
                    _logger.LogInformation("Transfer completed for SessionId={SessionId}", sessionId);
                    return new AgentResponse("Completed", Response: outcome.Message, SessionId: sessionId.ToString());

                case TransferOutcomeKind.Rejected:
                    return new AgentResponse("Error", outcome.Message, sessionId.ToString());

                case TransferOutcomeKind.RequiresApproval when _hitlState.IsSuspended:
                    // 3. Persist the suspended context BEFORE releasing the HTTP thread with 202.
                    await _hitlSessionRepository.CreatePendingAsync(
                        new HitlSession(
                            sessionId,
                            command.TenantId,
                            _hitlState.Reason,
                            JsonSerializer.Serialize(command),
                            HitlSessionStatus.PendingReview,
                            _timeProvider.GetUtcNow()),
                        cancellationToken);

                    _logger.LogWarning("Transfer suspended for SessionId={SessionId}: {Reason}", sessionId, _hitlState.Reason);
                    return new AgentResponse("Suspended", _hitlState.Reason, sessionId.ToString());

                default:
                    throw new InvalidOperationException("The plugin required approval without setting the HITL state.");
            }
        }

        public async Task<AgentResponse> ResumeAsync(Guid sessionId, bool isApproved, CancellationToken cancellationToken = default)
        {
            var tenantId = _tenantAccessor.GetCurrentTenantId();

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

            return isApproved
                ? new AgentResponse("Queued", "The approved transfer was queued for background processing.", sessionId.ToString())
                : new AgentResponse("Rejected", "The pending transfer was rejected.", sessionId.ToString());
        }
    }
}
