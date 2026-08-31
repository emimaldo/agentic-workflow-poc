using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Api.Controllers;
using AgenticWorkflowPoC.Plugins;
using AgenticWorkflowPoC.Plugins.Operations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Xunit;

namespace AgenticWorkflowPoC.Tests.Controllers
{
    public class AgentControllerUnitTests
    {
        [Fact]
        public async Task InvokeAgent_WhenModelExtractsConflict_ReturnsAccepted()
        {
            // Arrange
            var hitl = new HitlStateService();
            var fakeChat = new FakeChatCompletionService(returnConflict: true);

            var controller = new AgentController(fakeChat, hitl, NullLogger<AgentController>.Instance);

            // Prepare RequestServices so controller can resolve the plugin with the same hitl instance
            var services = new ServiceCollection();
            services.AddSingleton<StaffOverridesPlugin>(sp => new StaffOverridesPlugin(NullLogger<StaffOverridesPlugin>.Instance, hitl));
            var provider = services.BuildServiceProvider();

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = provider }
            };

            var req = new AgentController.AgentRequest("s1", "Please change EMP-REQ-001 availability to 2026-09-01T09:00:00Z");

            // Act
            var result = await controller.InvokeAgent(req);

            // Assert
            var accepted = Assert.IsType<AcceptedResult>(result);
            // ensure HITL state was set
            Assert.True(hitl.IsSuspended);
            Assert.False(string.IsNullOrWhiteSpace(hitl.Reason));
        }
    }

    // Fake implementation matching the real IChatCompletionService signatures
    internal class FakeChatCompletionService : IChatCompletionService
    {
        private readonly bool _returnConflict;

        public FakeChatCompletionService(bool returnConflict)
        {
            _returnConflict = returnConflict;
        }

        public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

        public Task<IReadOnlyList<Microsoft.SemanticKernel.ChatMessageContent>> GetChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, CancellationToken cancellationToken = default)
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

        public IAsyncEnumerable<Microsoft.SemanticKernel.StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, CancellationToken cancellationToken = default)
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
