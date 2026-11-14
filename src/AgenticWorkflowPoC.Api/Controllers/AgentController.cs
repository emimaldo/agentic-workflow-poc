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
                    // Out-of-band suspension: release the thread and point to the session resource.
                    AcceptedAtRoute(
                        TenantSessionsController.GetSessionRouteName,
                        new { tenantId = suspended.TenantId, sessionId = suspended.SessionId },
                        new SessionResponse(suspended.SessionId, "Suspended", suspended.Reason)),
                RoutingOutcome.Rejected rejected =>
                    UnprocessableProblem(rejected),
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
