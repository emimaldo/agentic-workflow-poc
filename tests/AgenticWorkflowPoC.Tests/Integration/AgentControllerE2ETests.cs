using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Api.Development;
using AgenticWorkflowPoC.Core.Entities;
using AgenticWorkflowPoC.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
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
        public async Task HighValueTransfer_SuspendsThenExecutesAfterAdminApproval()
        {
            var scenario = new TransferScenario(FakeChatClient.HighValueTransfer());
            var factory = ApiFactory.Create(_factory, scenario);
            var sessionId = Guid.NewGuid();
            var payload = new { SessionId = sessionId, Prompt = FakeChatClient.HighValueIntent };

            var anonymous = await ApiFactory.CreateClient(factory, null).PostAsJsonAsync("/api/agent/invoke", payload);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

            var admin = ApiFactory.CreateClient(factory, ApiFactory.AdminKeyA);
            var invoke = await admin.PostAsJsonAsync("/api/agent/invoke", payload);
            Assert.Equal(HttpStatusCode.Accepted, invoke.StatusCode);
            Assert.Contains("Suspended", await invoke.Content.ReadAsStringAsync());
            Assert.Equal(0, scenario.Ledger.ExecutedCount);

            var user = ApiFactory.CreateClient(factory, ApiFactory.UserKeyA);
            var forbidden = await user.PostAsJsonAsync($"/api/agent/resume/{sessionId}", new { IsApproved = true });
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

            var resume = await admin.PostAsJsonAsync($"/api/agent/resume/{sessionId}", new { IsApproved = true });
            Assert.Equal(HttpStatusCode.Accepted, resume.StatusCode);
            Assert.Contains("Queued", await resume.Content.ReadAsStringAsync());

            var finalSession = await WaitForStatusAsync(scenario, sessionId, HitlSessionStatus.Completed, TimeSpan.FromSeconds(5));
            Assert.Equal(HitlSessionStatus.Completed, finalSession?.Status);
            Assert.Equal(1, scenario.Ledger.ExecutedCount);
            Assert.Equal(1, scenario.Chat.CallCount);
        }

        [Fact]
        public async Task ResumeAgent_WhenSessionBelongsToAnotherTenant_ReturnsNotFound()
        {
            var scenario = new TransferScenario();
            var sessionId = Guid.NewGuid();
            await scenario.Repository.CreatePendingAsync(scenario.PendingSession(sessionId));
            var factory = ApiFactory.Create(_factory, scenario);

            var response = await ApiFactory.CreateClient(factory, ApiFactory.AdminKeyB)
                .PostAsJsonAsync($"/api/agent/resume/{sessionId}", new { IsApproved = true });

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(HitlSessionStatus.PendingReview, (await scenario.Repository.GetAsync(TransferScenario.TenantA, sessionId))?.Status);
        }

        internal static async Task<HitlSession?> WaitForStatusAsync(
            TransferScenario scenario,
            Guid sessionId,
            HitlSessionStatus expected,
            TimeSpan timeout)
        {
            var deadline = DateTimeOffset.UtcNow + timeout;
            HitlSession? session = null;
            while (DateTimeOffset.UtcNow < deadline)
            {
                session = await scenario.Repository.GetAsync(TransferScenario.TenantA, sessionId);
                if (session?.Status == expected)
                {
                    break;
                }

                await Task.Delay(25);
            }

            return session;
        }
    }
}
