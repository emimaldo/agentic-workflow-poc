using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Api.Models;
using AgenticWorkflowPoC.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkflowPoC.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AgentController : ControllerBase
    {
        internal const int MaxPromptLength = 2000;

        private readonly IAgentService _agentService;

        public AgentController(IAgentService agentService)
        {
            _agentService = agentService;
        }

        [HttpPost("invoke")]
        public async Task<IActionResult> InvokeAgent([FromBody] AgentRequest request, CancellationToken cancellationToken)
        {
            if (request.SessionId == Guid.Empty
                || string.IsNullOrWhiteSpace(request.Prompt)
                || request.Prompt.Length > MaxPromptLength)
            {
                ModelState.AddModelError(nameof(request.Prompt), $"A session id and a prompt of at most {MaxPromptLength} characters are required.");
                return ValidationProblem(ModelState);
            }

            var outcome = await _agentService.InvokeAsync(request.SessionId, request.Prompt, cancellationToken);

            // Closed routing: every outcome maps to exactly one status code.
            return outcome switch
            {
                RoutingOutcome.Completed completed =>
                    Ok(new SessionResponse(completed.SessionId, "Completed", completed.Message)),
                RoutingOutcome.Suspended suspended =>
                    Accepted(new SessionResponse(suspended.SessionId, "Suspended", suspended.Reason)),
                RoutingOutcome.Rejected rejected =>
                    UnprocessableProblem(rejected),
                _ => throw new UnreachableException()
            };
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("resume/{sessionId}")]
        public async Task<IActionResult> ResumeAgent(Guid sessionId, [FromBody] ResumeRequest request, CancellationToken cancellationToken)
        {
            var outcome = await _agentService.ResumeAsync(sessionId, request.IsApproved, cancellationToken);

            return outcome switch
            {
                ResumeOutcome.Queued queued =>
                    Accepted(new SessionResponse(queued.SessionId, "Queued", "The approved transfer was queued for background processing.")),
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

        private ObjectResult UnprocessableProblem(RoutingOutcome.Rejected rejected)
        {
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "The request was rejected by deterministic validation.",
                Type = "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.21",
                Extensions =
                {
                    ["sessionId"] = rejected.SessionId,
                    ["errors"] = rejected.Errors
                }
            };

            return new ObjectResult(problem)
            {
                StatusCode = StatusCodes.Status422UnprocessableEntity,
                ContentTypes = { "application/problem+json" }
            };
        }
    }
}
