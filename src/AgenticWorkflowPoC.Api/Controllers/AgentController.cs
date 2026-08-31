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
        private readonly Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService _chatCompletionService;
        private readonly IHitlState _hitlState;
        private readonly Microsoft.Extensions.Logging.ILogger<AgentController> _logger;
        // Staff Note: In a production environment, you would inject the IAgentSessionRepository here
        // to save and retrieve the ChatHistory from SQL Server.

        public AgentController( Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService chatCompletionService, IHitlState hitlState, Microsoft.Extensions.Logging.ILogger<AgentController> logger )
        {
            _chatCompletionService = chatCompletionService;
            _hitlState = hitlState;
            _logger = logger;
        }

        // Input DTOs
        public record AgentRequest( string SessionId, string Prompt );
        public record ResumeRequest( bool IsApproved );
        public record AgentResponse(string Status, string? Message = null, string? SessionId = null, string? Response = null);

        [HttpPost( "invoke" )]
        public async Task<IActionResult> InvokeAgent( [FromBody] AgentRequest request )
        {
            // Initialize scoped HITL state for this request
            _hitlState.IsSuspended = false;
            _hitlState.Reason = string.Empty;
            _logger.LogInformation("InvokeAgent called for SessionId={SessionId}", request.SessionId);

            var chatHistory = new ChatHistory(
                "You are a data extraction API. Extract the staff ID and date from the user's prompt. " +
                "Respond ONLY with a valid JSON object in this exact format, with no markdown, no code blocks, and no extra text: " +
                "{\"staffId\": \"extracted_id\", \"date\": \"extracted_date\"}. " +
                "If no staff ID is found, return {\"error\": \"missing_data\"}."
            );
            chatHistory.AddUserMessage( request.Prompt );

            var executionSettings = new PromptExecutionSettings();

            var result = await _chatCompletionService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                null
            );
            _logger.LogDebug("Model returned content: {Content}", result?.Content ?? "<null>");

            if( result?.Content == null || string.IsNullOrWhiteSpace( result.Content ) )
            {
                return BadRequest( new { error = "No usable JSON was returned by the model. Please provide the staff ID and date clearly." } );
            }

            try
            {
                using var jsonDoc = JsonDocument.Parse( result.Content );
                var root = jsonDoc.RootElement;

                if( root.TryGetProperty( "error", out var errorProp ) )
                {
                    return BadRequest( new { error = "missing_data", message = "Please provide a staff ID and date in a clearer format." } );
                }

                if( !root.TryGetProperty( "staffId", out var staffIdProp ) || !root.TryGetProperty( "date", out var dateProp ) )
                {
                    return BadRequest( new { error = "missing_data", message = "Please provide both the staff ID and the date." } );
                }

                var staffId = staffIdProp.GetString();
                var date = dateProp.GetString();

                if( string.IsNullOrWhiteSpace( staffId ) || string.IsNullOrWhiteSpace( date ) )
                {
                    return BadRequest( new { error = "missing_data", message = "The model returned incomplete extraction data." } );
                }

                _logger.LogInformation("Invoking StaffOverridesPlugin for staffId={StaffId} date={Date}", staffId, date);
                var plugin = HttpContext.RequestServices.GetRequiredService<AgenticWorkflowPoC.Plugins.Operations.StaffOverridesPlugin>();
                var pluginResult = await plugin.OverrideAvailabilityAsync( staffId, date );
                _logger.LogInformation("Plugin result: {PluginResult}", pluginResult);

                if( pluginResult != null && pluginResult.Contains( "SYSTEM INSTRUCTION: STOP" ) )
                {
                    _logger.LogWarning("Operation suspended for SessionId={SessionId}: {Reason}", request.SessionId, _hitlState.Reason);
                    return Accepted( new AgentResponse( "Suspended", _hitlState.Reason, request.SessionId ) );
                }

                _logger.LogInformation("Operation completed for SessionId={SessionId}", request.SessionId);
                return Ok( new AgentResponse( "Completed", Response: pluginResult ) );
            }
            catch( JsonException )
            {
                return BadRequest( new { error = "invalid_json", message = "The model did not return valid JSON." } );
            }
        }

        [HttpPost( "resume/{sessionId}" )]
        public async Task<IActionResult> ResumeAgent( string sessionId, [FromBody] ResumeRequest request )
        {
            var reconstructedHistory = new ChatHistory();

            string resolutionContext = request.IsApproved
                ? "SYSTEM_ALERT: The administrator has APPROVED the action. Proceed to confirm the success of the operation."
                : "SYSTEM_ALERT: The administrator has REJECTED the action. Inform the user and mark the flow as canceled.";

            reconstructedHistory.AddSystemMessage( resolutionContext );

            var executionSettings = new PromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            };

            var response = await _chatCompletionService.GetChatMessageContentAsync(
                reconstructedHistory,
                executionSettings,
                null
            );

            var resumed = new AgentResponse( "Completed", Response: response.Content );
            return Ok( resumed );
        }
    }
}
