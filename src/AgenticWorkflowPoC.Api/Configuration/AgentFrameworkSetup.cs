using System;
using AgenticWorkflowPoC.Api.Services;
using AgenticWorkflowPoC.Plugins;
using AgenticWorkflowPoC.Plugins.Operations;
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
            services.AddTransient<StaffOverridesPlugin>();

            services.AddSingleton<IChatClient>(_ => new OllamaApiClient(new Uri(endpoint), modelId));
            services.AddSingleton<AIAgent>(sp => sp
                .GetRequiredService<IChatClient>()
                .AsAIAgent(
                    instructions: AgentDefaults.ExtractionPrompt,
                    name: "AgenticWorkflowExtractor"));

            services.AddTransient<IAgentService, AgentService>();
            services.AddSingleton<IExtractionValidator, ExtractionValidator>();
            services.AddScoped<HitlWorkProcessor>();

            return services;
        }
    }
}
