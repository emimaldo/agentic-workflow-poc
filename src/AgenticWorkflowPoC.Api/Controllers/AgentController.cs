using System;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkflowPoC.Api.Controllers
{
    [ApiController]
    [Route( "api/[controller]" )]
    [Authorize]
    public class AgentController : ControllerBase
    {
        private readonly AgenticWorkflowPoC.Api.Services.IAgentService _agentService;
        private readonly Microsoft.Extensions.Logging.ILogger<AgentController> _logger;
        // HITL state and pending actions are managed by the service layer.

        public AgentController( AgenticWorkflowPoC.Api.Services.IAgentService agentService, Microsoft.Extensions.Logging.ILogger<AgentController> logger )
        {
            _agentService = agentService;
            _logger = logger;
        }


        [HttpPost( "invoke" )]
        public async Task<IActionResult> InvokeAgent( [FromBody] AgenticWorkflowPoC.Api.Models.AgentRequest request, CancellationToken cancellationToken )
        {
            var response = await _agentService.InvokeAsync(request.SessionId, request.Prompt, cancellationToken);

            if (response.Status == "Suspended")
            {
                return Accepted(response);
            }

            if (response.Status == "Error")
            {
                return BadRequest(new { error = response.Message });
            }

            return Ok(response);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost( "resume/{sessionId}" )]
        public async Task<IActionResult> ResumeAgent( Guid sessionId, [FromBody] AgenticWorkflowPoC.Api.Models.ResumeRequest request, CancellationToken cancellationToken )
        {
            var response = await _agentService.ResumeAsync(sessionId, request.IsApproved, cancellationToken);

            return response.Status switch
            {
                "NotFound" => NotFound(response),
                "Conflict" => Conflict(response),
                "Error" => BadRequest(response),
                "Queued" => Accepted(response),
                _ => Ok(response)
            };
        }
    }
}
