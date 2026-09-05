using System;
using System.Text.Json;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Api.Models;
using AgenticWorkflowPoC.Plugins;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AgenticWorkflowPoC.Api.Services
{
    public class AgentService : IAgentService
    {
        private readonly Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService _chatCompletionService;
        private readonly IHitlState _hitlState;
        private readonly ILogger<AgentService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IExtractionValidator _validator;

        public AgentService(Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService chatCompletionService, IHitlState hitlState, ILogger<AgentService> logger, IServiceProvider serviceProvider, IExtractionValidator validator)
        {
            _chatCompletionService = chatCompletionService;
            _hitlState = hitlState;
            _logger = logger;
            _serviceProvider = serviceProvider;
            _validator = validator;
        }

        public async Task<AgentResponse> InvokeAsync(string sessionId, string prompt)
        {
            _hitlState.IsSuspended = false;
            _hitlState.Reason = string.Empty;
            _logger.LogInformation("InvokeAgent called for SessionId={SessionId}", sessionId);

            var chatHistory = new ChatHistory(AgentDefaults.ExtractionPrompt);
            chatHistory.AddUserMessage(prompt);

            var executionSettings = new PromptExecutionSettings();

            var contents = await _chatCompletionService.GetChatMessageContentsAsync(
                chatHistory,
                executionSettings,
                null
            );

            var result = contents.Count > 0 ? contents[0] : null;
            _logger.LogDebug("Model returned content: {Content}", result?.Content ?? "<null>");

            if (result?.Content == null || string.IsNullOrWhiteSpace(result.Content))
            {
                return new AgentResponse("Error", "No usable JSON was returned by the model. Please provide the staff ID and date clearly.", sessionId);
            }

            try
            {
                using var jsonDoc = JsonDocument.Parse(result.Content);
                var validation = _validator.Validate(result.Content);

                if (!validation.IsValid)
                {
                    return new AgentResponse("Error", validation.Error ?? "missing_data", sessionId);
                }

                var staffId = validation.StaffId!;
                var date = validation.Date!;

                _logger.LogInformation("Invoking StaffOverridesPlugin for staffId={StaffId} date={Date}", staffId, date);
                var plugin = (AgenticWorkflowPoC.Plugins.Operations.StaffOverridesPlugin)_serviceProvider.GetRequiredService(typeof(AgenticWorkflowPoC.Plugins.Operations.StaffOverridesPlugin));
                var pluginResult = await plugin.OverrideAvailabilityAsync(staffId, date);
                _logger.LogInformation("Plugin result: {PluginResult}", pluginResult);

                if (_hitlState.IsSuspended)
                {
                    _logger.LogWarning("Operation suspended for SessionId={SessionId}: {Reason}", sessionId, _hitlState.Reason);
                    return new AgentResponse("Suspended", _hitlState.Reason, sessionId);
                }

                _logger.LogInformation("Operation completed for SessionId={SessionId}", sessionId);
                return new AgentResponse("Completed", Response: pluginResult);
            }
            catch (JsonException)
            {
                return new AgentResponse("Error", "invalid_json: The model did not return valid JSON.", sessionId);
            }
        }

        public async Task<AgentResponse> ResumeAsync(string sessionId, bool isApproved)
        {
            var reconstructedHistory = new ChatHistory();

            string resolutionContext = isApproved
                ? "SYSTEM_ALERT: The administrator has APPROVED the action. Proceed to confirm the success of the operation."
                : "SYSTEM_ALERT: The administrator has REJECTED the action. Inform the user and mark the flow as canceled.";

            reconstructedHistory.AddSystemMessage(resolutionContext);

            var executionSettings = new PromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            };

            var response = await _chatCompletionService.GetChatMessageContentAsync(
                reconstructedHistory,
                executionSettings,
                null
            );

            var resumed = new AgentResponse("Completed", Response: response.Content);
            return resumed;
        }
    }
}
