using System.Text.Json;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Plugins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AgenticWorkflowPoC.Api.Controllers
{
    [ApiController]
    [Route( "api/[controller]" )]
    public class AgentController : ControllerBase
    {
        private readonly AgenticWorkflowPoC.Api.Services.IAgentService _agentService;
        private readonly Microsoft.Extensions.Logging.ILogger<AgentController> _logger;
        // Staff Note: In a production environment, you would inject the IAgentSessionRepository here
        // to save and retrieve the ChatHistory from SQL Server.

        public AgentController( AgenticWorkflowPoC.Api.Services.IAgentService agentService, Microsoft.Extensions.Logging.ILogger<AgentController> logger )
        {
            _agentService = agentService;
            _logger = logger;
        }


        [HttpPost( "invoke" )]
        public async Task<IActionResult> InvokeAgent( [FromBody] AgenticWorkflowPoC.Api.Models.AgentRequest request )
        {
            var response = await _agentService.InvokeAsync(request.SessionId, request.Prompt);

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

        [HttpPost( "resume/{sessionId}" )]
        public async Task<IActionResult> ResumeAgent( string sessionId, [FromBody] AgenticWorkflowPoC.Api.Models.ResumeRequest request )
        {
            var response = await _agentService.ResumeAsync(sessionId, request.IsApproved);
            return Ok(response);
        }
    }
}
