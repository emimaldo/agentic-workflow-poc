using System.Net.Http.Json;
using System;
using System.Net;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Api.Development;
using AgenticWorkflowPoC.Core.Interfaces;
using AgenticWorkflowPoC.Plugins;
using AgenticWorkflowPoC.Plugins.Operations;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.AI;
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
            var sessionRepository = new InMemoryHitlSessionRepository();
            var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Replace the MEAI client with a deterministic fake
                    services.RemoveAll<IChatClient>();
                    services.AddSingleton<IChatClient>(new FakeChatClient(returnConflict: true));
                    services.RemoveAll<IHitlSessionRepository>();
                    services.AddSingleton<IHitlSessionRepository>(sessionRepository);
                });
            });

            var client = factory.CreateClient();
            var sessionId = Guid.NewGuid();

            var payload = new { SessionId = sessionId, Prompt = "Please change EMP-REQ-001 availability to 2026-09-01T09:00:00Z" };

            var resp = await client.PostAsJsonAsync("/api/agent/invoke", payload);

            var json = await resp.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);
            Assert.Contains("Suspended", json);

            var resumeResponse = await client.PostAsJsonAsync($"/api/agent/resume/{sessionId}", new { IsApproved = true });
            var resumedJson = await resumeResponse.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, resumeResponse.StatusCode);
            Assert.Contains("Completed", resumedJson);
        }
    }
}
