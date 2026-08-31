using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Plugins;
using AgenticWorkflowPoC.Plugins.Operations;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Xunit;

namespace AgenticWorkflowPoC.Tests.Integration
{
    public class AgentControllerE2ETests : IClassFixture<WebApplicationFactory<AgenticWorkflowPoC.Api.Program>>
    {
        private readonly WebApplicationFactory<AgenticWorkflowPoC.Api.Program> _factory;

        public AgentControllerE2ETests(WebApplicationFactory<AgenticWorkflowPoC.Api.Program> factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task InvokeAgent_E2E_ReturnsAccepted_WhenPluginSuspends()
        {
            var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Replace the IChatCompletionService with a deterministic fake
                    services.RemoveAll<IChatCompletionService>();
                    services.AddSingleton<IChatCompletionService>(new FakeChatCompletionService(returnConflict: true));
                });
            });

            var client = factory.CreateClient();

            var payload = new { SessionId = "s-e2e-1", Prompt = "Please change EMP-REQ-001 availability to 2026-09-01T09:00:00Z" };

            var resp = await client.PostAsJsonAsync("/api/agent/invoke", payload);

            var json = await resp.Content.ReadAsStringAsync();
            Assert.True(resp.IsSuccessStatusCode);
            Assert.Contains("Suspended", json);
        }
    }

    // Simple deterministic fake for the IChatCompletionService used in E2E tests
    internal class FakeChatCompletionService : IChatCompletionService
    {
        private readonly bool _returnConflict;

        public FakeChatCompletionService(bool returnConflict)
        {
            _returnConflict = returnConflict;
        }

        public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

        public Task<IReadOnlyList<Microsoft.SemanticKernel.ChatMessageContent>> GetChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, System.Threading.CancellationToken cancellationToken = default)
        {
            var content = new Microsoft.SemanticKernel.ChatMessageContent();
            if (_returnConflict)
            {
                content.Content = "{\"staffId\": \"EMP-REQ-001\", \"date\": \"2026-09-01T09:00:00Z\"}";
            }
            else
            {
                content.Content = "{\"staffId\": \"EMP-102\", \"date\": \"2026-09-01T09:00:00Z\"}";
            }

            IReadOnlyList<Microsoft.SemanticKernel.ChatMessageContent> list = new List<Microsoft.SemanticKernel.ChatMessageContent> { content };
            return Task.FromResult(list);
        }

        public IAsyncEnumerable<Microsoft.SemanticKernel.StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, System.Threading.CancellationToken cancellationToken = default)
        {
            return StreamImpl();

            async IAsyncEnumerable<Microsoft.SemanticKernel.StreamingChatMessageContent> StreamImpl()
            {
                await System.Threading.Tasks.Task.Yield();
                yield break;
            }
        }
    }
}
