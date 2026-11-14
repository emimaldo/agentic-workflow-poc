using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
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
    /// <summary>
    /// Deterministic router: LLM extraction → validation → C# domain plugin → closed outcome.
    /// </summary>
    public class AgentService : IAgentService
    {
        private readonly IExtractionValidator<EnrichedTransferCommand> _validator;
        private readonly FinancialTransferPlugin _transferPlugin;
        private readonly IHitlState _hitlState;
        private readonly IHitlSessionRepository _hitlSessionRepository;
        private readonly ITenantAccessor _tenantAccessor;
        private readonly ChannelWriter<ResumedWorkflowEvent> _resumeSignals;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<AgentService> _logger;

        public AgentService(
            IExtractionValidator<EnrichedTransferCommand> validator,
            FinancialTransferPlugin transferPlugin,
            IHitlState hitlState,
            IHitlSessionRepository hitlSessionRepository,
            ITenantAccessor tenantAccessor,
            ChannelWriter<ResumedWorkflowEvent> resumeSignals,
            TimeProvider timeProvider,
            ILogger<AgentService> logger)
        {
            _validator = validator;
            _transferPlugin = transferPlugin;
            _hitlState = hitlState;
            _hitlSessionRepository = hitlSessionRepository;
            _tenantAccessor = tenantAccessor;
            _resumeSignals = resumeSignals;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        public async Task<RoutingOutcome> InvokeAsync(Guid sessionId, string prompt, CancellationToken cancellationToken = default)
        {
            if (sessionId == Guid.Empty)
            {
                throw new ArgumentException("A non-empty session id is required.", nameof(sessionId));
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

            // 1. LLM as a pure parser, intercepted by the deterministic validator.
            var extraction = await _validator.ExtractAndValidateAsync(prompt, cancellationToken);
            if (!extraction.IsValid)
            {
                return new RoutingOutcome.Rejected(
                    sessionId,
                    [.. extraction.Errors.Select(e => new RoutingError(e.Code.ToString(), e.Message))]);
            }

            var command = extraction.Data;

            // 2. C# owns execution and routing.
            var outcome = await _transferPlugin.ExecuteTransferAsync(sessionId, command, cancellationToken);

            switch (outcome.Kind)
            {
                case TransferOutcomeKind.Completed:
                    _logger.LogInformation("Transfer completed for SessionId={SessionId}", sessionId);
                    return new RoutingOutcome.Completed(sessionId, outcome.Message);

                case TransferOutcomeKind.Rejected:
                    return new RoutingOutcome.Rejected(
                        sessionId,
                        [new RoutingError(outcome.RejectionCode?.ToString() ?? "DomainRejected", outcome.Message)]);

                case TransferOutcomeKind.RequiresApproval when _hitlState is { IsSuspended: true, Context: { } context }:
                    // 3. Persist the immutable suspended context BEFORE releasing the HTTP thread with 202.
                    await _hitlSessionRepository.CreatePendingAsync(
                        new HitlSession(
                            sessionId,
                            command.TenantId,
                            _hitlState.Reason,
                            JsonSerializer.Serialize(context, context.GetType()),
                            HitlSessionStatus.PendingReview,
                            _timeProvider.GetUtcNow()),
                        cancellationToken);

                    _logger.LogWarning("Transfer suspended for SessionId={SessionId}: {Reason}", sessionId, _hitlState.Reason);
                    return new RoutingOutcome.Suspended(sessionId, command.TenantId, _hitlState.Reason);

                default:
                    throw new InvalidOperationException("The plugin required approval without setting the HITL state.");
            }
        }

        public async Task<ResumeOutcome> ResumeAsync(Guid sessionId, bool isApproved, CancellationToken cancellationToken = default)
        {
            var tenantId = _tenantAccessor.GetCurrentTenantId();

            var pendingSession = await _hitlSessionRepository.GetAsync(tenantId, sessionId, cancellationToken);
            if (pendingSession is null)
            {
                return new ResumeOutcome.NotFound(sessionId);
            }

            if (pendingSession.Status != HitlSessionStatus.PendingReview)
            {
                return new ResumeOutcome.AlreadyResolved(sessionId, pendingSession.Status);
            }

            // The resume signal is a hint, not an authorization: the state transition is persisted
            // atomically (compare-and-set) and execution happens later, after re-evaluation.
            var resolvedStatus = isApproved ? HitlSessionStatus.Approved : HitlSessionStatus.Rejected;
            var transitioned = await _hitlSessionRepository.TryResolveAsync(
                tenantId,
                sessionId,
                resolvedStatus,
                _tenantAccessor.GetCurrentSubjectId(),
                _timeProvider.GetUtcNow(),
                cancellationToken);

            if (!transitioned)
            {
                var current = await _hitlSessionRepository.GetAsync(tenantId, sessionId, cancellationToken);
                return new ResumeOutcome.AlreadyResolved(sessionId, current?.Status ?? resolvedStatus);
            }

            if (!isApproved)
            {
                return new ResumeOutcome.RejectedByOperator(sessionId);
            }

            // SQL first, signal second. If this write is lost (crash, cancelled request), the worker's
            // periodic sweep still picks the approved session up from the durable queue.
            await _resumeSignals.WriteAsync(new ResumedWorkflowEvent(tenantId, sessionId), cancellationToken);
            return new ResumeOutcome.Queued(sessionId, tenantId);
        }
    }
}
