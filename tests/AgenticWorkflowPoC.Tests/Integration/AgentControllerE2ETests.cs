using System.Net.Http.Json;
using System;
using System.Net;
using System.Collections.Generic;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Api.Development;
using AgenticWorkflowPoC.Core.Interfaces;
using AgenticWorkflowPoC.Core.Entities;
using AgenticWorkflowPoC.Plugins;
using AgenticWorkflowPoC.Plugins.Operations;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using AgenticWorkflowPoC.Api.Security;
using Microsoft.AspNetCore.Hosting;
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
            var availabilityExecutor = new InMemoryStaffAvailabilityExecutor();
            var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["USE_FAKE_CHAT"] = "true",
                        ["Authentication:ApiKeys:0:Key"] = "test-admin-key",
                        ["Authentication:ApiKeys:0:TenantId"] = "tenant-a",
                        ["Authentication:ApiKeys:0:Role"] = "Admin",
                        ["Authentication:ApiKeys:1:Key"] = "other-tenant-key",
                        ["Authentication:ApiKeys:1:TenantId"] = "tenant-b",
                        ["Authentication:ApiKeys:1:Role"] = "Admin",
                        ["Authentication:ApiKeys:2:Key"] = "tenant-user-key",
                        ["Authentication:ApiKeys:2:TenantId"] = "tenant-a",
                        ["Authentication:ApiKeys:2:Role"] = "User"
                    });
                });
                builder.ConfigureServices(services =>
                {
                    // Replace the MEAI client with a deterministic fake
                    services.RemoveAll<IChatClient>();
                    services.AddSingleton<IChatClient>(new FakeChatClient(returnConflict: true));
                    services.RemoveAll<IHitlSessionRepository>();
                    services.AddSingleton<IHitlSessionRepository>(sessionRepository);
                    services.RemoveAll<IStaffAvailabilityExecutor>();
                    services.AddSingleton<IStaffAvailabilityExecutor>(availabilityExecutor);
                });
            });

            var client = factory.CreateClient();
            var sessionId = Guid.NewGuid();

            var payload = new { SessionId = sessionId, Prompt = "Please change EMP-REQ-001 availability to 2026-09-01T09:00:00Z" };

            var resp = await client.PostAsJsonAsync("/api/agent/invoke", payload);
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);

            client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.ApiKeyHeaderName, "test-admin-key");
            resp = await client.PostAsJsonAsync("/api/agent/invoke", payload);

            var json = await resp.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);
            Assert.Contains("Suspended", json);

            var userClient = factory.CreateClient();
            userClient.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.ApiKeyHeaderName, "tenant-user-key");
            var forbiddenResponse = await userClient.PostAsJsonAsync(
                $"/api/agent/resume/{sessionId}",
                new { IsApproved = true });
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);

            var resumeResponse = await client.PostAsJsonAsync($"/api/agent/resume/{sessionId}", new { IsApproved = true });
            var queuedJson = await resumeResponse.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.Accepted, resumeResponse.StatusCode);
            Assert.Contains("Queued", queuedJson);

            var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
            HitlSession? finalSession = null;
            while (DateTimeOffset.UtcNow < deadline)
            {
                finalSession = await sessionRepository.GetAsync("tenant-a", sessionId);
                if (finalSession?.Status == HitlSessionStatus.Completed)
                {
                    break;
                }

                await Task.Delay(50);
            }

            Assert.Equal(HitlSessionStatus.Completed, finalSession?.Status);
            Assert.Equal(1, availabilityExecutor.ExecutionCount);
        }

        [Fact]
        public async Task ResumeAgent_WhenSessionBelongsToAnotherTenant_ReturnsNotFound()
        {
            var sessionRepository = new InMemoryHitlSessionRepository();
            var sessionId = Guid.NewGuid();
            await sessionRepository.CreatePendingAsync(new HitlSession(
                sessionId,
                "tenant-a",
                "Requires review.",
                "{\"StaffId\":\"EMP-REQ-001\",\"NewAvailability\":\"2026-09-01T09:00:00+00:00\"}",
                HitlSessionStatus.PendingReview,
                DateTimeOffset.UtcNow));

            var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["USE_FAKE_CHAT"] = "true",
                        ["Authentication:ApiKeys:0:Key"] = "other-tenant-key",
                        ["Authentication:ApiKeys:0:TenantId"] = "tenant-b",
                        ["Authentication:ApiKeys:0:Role"] = "Admin"
                    });
                });
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IHitlSessionRepository>();
                    services.AddSingleton<IHitlSessionRepository>(sessionRepository);
                    services.RemoveAll<IStaffAvailabilityExecutor>();
                    services.AddSingleton<IStaffAvailabilityExecutor, InMemoryStaffAvailabilityExecutor>();
                });
            });

            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.ApiKeyHeaderName, "other-tenant-key");

            var response = await client.PostAsJsonAsync(
                $"/api/agent/resume/{sessionId}",
                new { IsApproved = true });

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(HitlSessionStatus.PendingReview, (await sessionRepository.GetAsync("tenant-a", sessionId))?.Status);
        }
    }
}
