using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Ollama;
using AgenticWorkflowPoC.Plugins;
using AgenticWorkflowPoC.Plugins.Operations;

namespace AgenticWorkflowPoC.Api.Configuration
{
    public static class SemanticKernelSetup
    {
        public static IServiceCollection AddAgenticWorkflow(this IServiceCollection services, string modelId, string endpoint)
        {
            // 1. Register native C# Plugins and scoped HITL state
            services.AddScoped<IHitlState, HitlStateService>();
            services.AddTransient<StaffOverridesPlugin>();

            // 2. Register the local Ollama chat completion service used by Semantic Kernel.
            services.AddSingleton<IAutoFunctionInvocationFilter, HitlTerminationFilter>();

            services.AddSingleton<Kernel>(sp =>
            {
                var builder = Kernel.CreateBuilder();

                builder.Services.AddSingleton<IAutoFunctionInvocationFilter, HitlTerminationFilter>();

                builder.AddOllamaChatCompletion(
                    modelId: modelId,
                    endpoint: new Uri(endpoint));

                // Note: do not inject plugin instances into kernel here to avoid resolving scoped services at singleton build time.

                return builder.Build();
            });

            // Expose the kernel's IChatCompletionService through DI so controllers can inject/mock it in tests.
            services.AddSingleton(sp => sp.GetRequiredService<Kernel>().GetRequiredService<Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService>());

            return services;
        }
    }
}