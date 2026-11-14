using System;
using AgenticWorkflowPoC.Api.Services;
using AgenticWorkflowPoC.Core.Entities;
using AgenticWorkflowPoC.Plugins;
using AgenticWorkflowPoC.Plugins.Financial;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OllamaSharp;

namespace AgenticWorkflowPoC.Api.Configuration
{
    public static class AgentFrameworkSetup
    {
        public static IServiceCollection AddAgenticWorkflow(this IServiceCollection services, string modelId, string endpoint)
        {
            services.AddScoped<IHitlState, HitlStateService>();
            services.AddScoped<FinancialTransferPlugin>();

            services.AddSingleton<IChatClient>(_ => new OllamaApiClient(new Uri(endpoint), modelId));
            services.AddSingleton<AIAgent>(sp => sp
                .GetRequiredService<IChatClient>()
                .AsAIAgent(
                    instructions: AgentDefaults.ExtractionPrompt,
                    name: "TransferIntentExtractor"));

            services.AddScoped<IExtractionValidator<EnrichedTransferCommand>, TransferExtractionValidator>();
            services.AddScoped<IAgentService, AgentService>();
            services.AddScoped<HitlWorkProcessor>();

            return services;
        }
    }
}
