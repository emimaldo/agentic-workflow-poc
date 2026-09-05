using System.Collections.Generic;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Api.Models;
using AgenticWorkflowPoC.Api.Services;
using AgenticWorkflowPoC.Plugins;
using AgenticWorkflowPoC.Plugins.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Xunit;

namespace AgenticWorkflowPoC.Tests.Services
{
    public class AgentServiceTests
    {
        [Fact]
        public async Task InvokeAsync_WhenModelReturnsConflict_ServiceReturnsSuspended()
        {
            var hitl = new HitlStateService();
            var fakeChat = new FakeChatCompletionService(returnConflict: true);

            var services = new ServiceCollection();
            services.AddSingleton<StaffOverridesPlugin>(sp => new StaffOverridesPlugin(NullLogger<StaffOverridesPlugin>.Instance, hitl));
            var provider = services.BuildServiceProvider();

            var validator = new ExtractionValidator();
            var agentService = new AgentService(fakeChat, hitl, NullLogger<AgentService>.Instance, provider, validator);

            var resp = await agentService.InvokeAsync("s1", "prompt");

            Assert.Equal("Suspended", resp.Status);
            Assert.False(string.IsNullOrWhiteSpace(resp.Message));
        }

        [Fact]
        public async Task InvokeAsync_WhenModelReturnsOk_ServiceReturnsCompleted()
        {
            var hitl = new HitlStateService();
            var fakeChat = new FakeChatCompletionService(returnConflict: false);

            var services = new ServiceCollection();
            services.AddSingleton<StaffOverridesPlugin>(sp => new StaffOverridesPlugin(NullLogger<StaffOverridesPlugin>.Instance, hitl));
            var provider = services.BuildServiceProvider();

            var validator = new ExtractionValidator();
            var agentService = new AgentService(fakeChat, hitl, NullLogger<AgentService>.Instance, provider, validator);

            var resp = await agentService.InvokeAsync("s2", "prompt");

            Assert.Equal("Completed", resp.Status);
            Assert.Contains("SUCCESS", resp.Response);
        }

        [Fact]
        public async Task ResumeAsync_ReturnsModelContent()
        {
            var hitl = new HitlStateService();
            var fakeChat = new FakeChatCompletionService(returnConflict: false, resumeContent: "OK_RESUME");
            var validator = new ExtractionValidator();
            var services = new ServiceCollection();
            var provider = services.BuildServiceProvider();

            var agentService = new AgentService(fakeChat, hitl, NullLogger<AgentService>.Instance, provider, validator);

            var resp = await agentService.ResumeAsync("s-1", true);

            Assert.Equal("Completed", resp.Status);
            Assert.Equal("OK_RESUME", resp.Response);
        }
    }

    internal class FakeChatCompletionService : IChatCompletionService
    {
        private readonly bool _returnConflict;
        private readonly string? _resumeContent;

        public FakeChatCompletionService(bool returnConflict, string? resumeContent = null)
        {
            _returnConflict = returnConflict;
            _resumeContent = resumeContent;
        }

        public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

        public Task<IReadOnlyList<Microsoft.SemanticKernel.ChatMessageContent>> GetChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, System.Threading.CancellationToken cancellationToken = default)
        {
            var content = new Microsoft.SemanticKernel.ChatMessageContent();

            if (!string.IsNullOrEmpty(_resumeContent))
            {
                content.Content = _resumeContent;
            }
            else if (_returnConflict)
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
