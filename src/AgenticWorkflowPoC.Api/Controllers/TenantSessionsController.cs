using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Api.Models;
using AgenticWorkflowPoC.Api.Security;
using AgenticWorkflowPoC.Api.Services;
using AgenticWorkflowPoC.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkflowPoC.Api.Controllers
{
    /// <summary>
    /// Administrative HITL surface (Chapter 3): status polling target of the 202 <c>Location</c>
    /// header, and the operator resume endpoint. The tenant in the route must match the
    /// authenticated tenant; otherwise the session simply does not exist (404, never 403).
    /// </summary>
    [ApiController]
    [Route("api/v1/tenants/{tenantId}/sessions")]
    [Authorize]
    public sealed class TenantSessionsController : ControllerBase
    {
        public const string GetSessionRouteName = "GetHitlSession";

        private readonly IAgentService _agentService;
        private readonly IHitlSessionRepository _repository;
        private readonly ITenantAccessor _tenantAccessor;

        public TenantSessionsController(
            IAgentService agentService,
            IHitlSessionRepository repository,
            ITenantAccessor tenantAccessor)
        {
            _agentService = agentService;
            _repository = repository;
            _tenantAccessor = tenantAccessor;
        }

        [HttpGet("{sessionId:guid}", Name = GetSessionRouteName)]
        public async Task<IActionResult> GetSession(string tenantId, Guid sessionId, CancellationToken cancellationToken)
        {
            if (!IsCurrentTenant(tenantId))
            {
                return NotFound();
            }

            var session = await _repository.GetAsync(tenantId, sessionId, cancellationToken);
            return session is null
                ? NotFound()
                : Ok(new HitlSessionView(
                    session.SessionId,
                    session.Status.ToString(),
                    session.Reason,
                    session.SuspendedAt,
                    session.ResolvedAt,
                    session.InvalidationReason));
        }

        [Authorize(Policy = AuthorizationPolicies.RequireAdminRole)]
        [HttpPost("{sessionId:guid}/resume")]
        public async Task<IActionResult> Resume(
            string tenantId,
            Guid sessionId,
            [FromBody] ResumeSessionRequest request,
            CancellationToken cancellationToken)
        {
            if (!IsCurrentTenant(tenantId))
            {
                return NotFound();
            }

            var outcome = await _agentService.ResumeAsync(sessionId, request.IsApproved, cancellationToken);

            return outcome switch
            {
                ResumeOutcome.Queued queued => AcceptedAtRoute(
                    GetSessionRouteName,
                    new { tenantId = queued.TenantId, sessionId = queued.SessionId },
                    new SessionResponse(queued.SessionId, "Queued", "The approved transfer was queued for background processing.")),
                ResumeOutcome.RejectedByOperator rejected =>
                    Ok(new SessionResponse(rejected.SessionId, "Rejected", "The pending transfer was rejected.")),
                ResumeOutcome.NotFound =>
                    NotFound(),
                ResumeOutcome.AlreadyResolved resolved =>
                    Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "The HITL session has already been resolved.",
                        detail: $"Current status: {resolved.CurrentStatus}."),
                _ => throw new UnreachableException()
            };
        }

        private bool IsCurrentTenant(string routeTenantId) =>
            string.Equals(routeTenantId, _tenantAccessor.GetCurrentTenantId(), StringComparison.Ordinal);
    }
}
