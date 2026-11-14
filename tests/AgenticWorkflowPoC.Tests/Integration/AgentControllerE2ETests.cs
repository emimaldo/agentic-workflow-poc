using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Api.Development;
using AgenticWorkflowPoC.Api.Models;
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
            var sessionPath = $"/api/v1/tenants/{TransferScenario.TenantA}/sessions/{sessionId}";

            var anonymous = await ApiFactory.CreateClient(factory, null).PostAsJsonAsync("/api/agent/invoke", payload);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

            // 1. Out-of-band suspension: 202 + Location pointing to the session resource.
            var user = ApiFactory.CreateClient(factory, ApiFactory.UserKeyA);
            var invoke = await user.PostAsJsonAsync("/api/agent/invoke", payload);
            Assert.Equal(HttpStatusCode.Accepted, invoke.StatusCode);
            Assert.Equal(sessionPath, invoke.Headers.Location?.AbsolutePath);
            Assert.Equal(0, scenario.Ledger.ExecutedCount);

            var status = await user.GetFromJsonAsync<HitlSessionView>(invoke.Headers.Location);
            Assert.Equal("PendingReview", status?.Status);

            // 2. Only operators (RequireAdminRole) can resume.
            var forbidden = await user.PostAsJsonAsync($"{sessionPath}/resume", new { IsApproved = true });
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

            var admin = ApiFactory.CreateClient(factory, ApiFactory.AdminKeyA);
            var resume = await admin.PostAsJsonAsync($"{sessionPath}/resume", new { IsApproved = true });
            Assert.Equal(HttpStatusCode.Accepted, resume.StatusCode);
            Assert.Equal(sessionPath, resume.Headers.Location?.AbsolutePath);

            // 3. Background execution, no further inference. The sweep is 60 s in these tests,
            //    so completing within the timeout proves the channel signal woke the worker.
            var finalSession = await WaitForStatusAsync(scenario, sessionId, HitlSessionStatus.Completed, TimeSpan.FromSeconds(5));
            Assert.Equal(HitlSessionStatus.Completed, finalSession?.Status);
            Assert.Equal(1, scenario.Ledger.ExecutedCount);
            Assert.Equal(1, scenario.Chat.CallCount);
            Assert.Equal("Completed", (await user.GetFromJsonAsync<HitlSessionView>(sessionPath))?.Status);

            var second = await admin.PostAsJsonAsync($"{sessionPath}/resume", new { IsApproved = true });
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        }

        [Fact]
        public async Task ApprovedSessionWithoutSignal_IsRecoveredBySweep()
        {
            // Simulates a lost signal: the approval is in the durable store but nothing was enqueued.
            var scenario = new TransferScenario();
            var factory = ApiFactory.Create(_factory, scenario, new Dictionary<string, string?>
            {
                ["HitlWorker:SweepInterval"] = "00:00:00.200"
            });
            _ = factory.Server; // start the host; the worker drains the (empty) queue and starts waiting
            await Task.Delay(100);

            var sessionId = Guid.NewGuid();
            await scenario.Repository.CreatePendingAsync(scenario.PendingSession(sessionId, status: HitlSessionStatus.Approved));

            var finalSession = await WaitForStatusAsync(scenario, sessionId, HitlSessionStatus.Completed, TimeSpan.FromSeconds(5));

            Assert.Equal(HitlSessionStatus.Completed, finalSession?.Status);
            Assert.Equal(1, scenario.Ledger.ExecutedCount);
        }

        [Fact]
        public async Task ApprovalAfterAccountFreeze_EndsInvalidatedAndIsVisibleToTheClient()
        {
            var scenario = new TransferScenario(FakeChatClient.HighValueTransfer());
            var factory = ApiFactory.Create(_factory, scenario);
            var sessionId = Guid.NewGuid();
            var user = ApiFactory.CreateClient(factory, ApiFactory.UserKeyA);
            var admin = ApiFactory.CreateClient(factory, ApiFactory.AdminKeyA);

            var invoke = await user.PostAsJsonAsync("/api/agent/invoke", new { SessionId = sessionId, Prompt = FakeChatClient.HighValueIntent });
            scenario.Ledger.Freeze(TransferScenario.TenantA, TransferScenario.OperatingAccount);
            await admin.PostAsJsonAsync($"{invoke.Headers.Location!.AbsolutePath}/resume", new { IsApproved = true });

            await WaitForStatusAsync(scenario, sessionId, HitlSessionStatus.Invalidated, TimeSpan.FromSeconds(5));
            var view = await user.GetFromJsonAsync<HitlSessionView>(invoke.Headers.Location);

            Assert.Equal("Invalidated", view?.Status);
            Assert.StartsWith("AccountFrozen", view?.InvalidationReason);
            Assert.Equal(0, scenario.Ledger.ExecutedCount);
        }

        [Fact]
        public async Task SessionEndpoints_AreInvisibleAcrossTenants()
        {
            var scenario = new TransferScenario();
            var sessionId = Guid.NewGuid();
            await scenario.Repository.CreatePendingAsync(scenario.PendingSession(sessionId));
            var factory = ApiFactory.Create(_factory, scenario);
            var tenantB = ApiFactory.CreateClient(factory, ApiFactory.AdminKeyB);

            // Tenant B addressing tenant A's route, and tenant B's own route with A's session id.
            var crossRoute = await tenantB.GetAsync($"/api/v1/tenants/{TransferScenario.TenantA}/sessions/{sessionId}");
            var ownRoute = await tenantB.GetAsync($"/api/v1/tenants/{TransferScenario.TenantB}/sessions/{sessionId}");
            var crossResume = await tenantB.PostAsJsonAsync(
                $"/api/v1/tenants/{TransferScenario.TenantA}/sessions/{sessionId}/resume", new { IsApproved = true });
            var ownResume = await tenantB.PostAsJsonAsync(
                $"/api/v1/tenants/{TransferScenario.TenantB}/sessions/{sessionId}/resume", new { IsApproved = true });

            Assert.Equal(HttpStatusCode.NotFound, crossRoute.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, ownRoute.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, crossResume.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, ownResume.StatusCode);
            Assert.Equal(HitlSessionStatus.PendingReview, (await scenario.Repository.GetAsync(TransferScenario.TenantA, sessionId))?.Status);
        }

        [Fact]
        public async Task LegacyResumeRoute_IsGone()
        {
            var factory = ApiFactory.Create(_factory, new TransferScenario());

            var response = await ApiFactory.CreateClient(factory, ApiFactory.AdminKeyA)
                .PostAsJsonAsync($"/api/agent/resume/{Guid.NewGuid()}", new { IsApproved = true });

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task LowValueTransfer_ReturnsOkAndDebitsImmediately()
        {
            var scenario = new TransferScenario(FakeChatClient.LowValueTransfer());
            var factory = ApiFactory.Create(_factory, scenario);

            var response = await ApiFactory.CreateClient(factory, ApiFactory.UserKeyA).PostAsJsonAsync(
                "/api/agent/invoke",
                new { SessionId = Guid.NewGuid(), Prompt = FakeChatClient.LowValueIntent });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Completed", await response.Content.ReadAsStringAsync());
            Assert.Equal(1, scenario.Ledger.ExecutedCount);
        }

        [Fact]
        public async Task HallucinatedField_Returns422ProblemDetailsWithoutSideEffects()
        {
            var scenario = new TransferScenario(new FakeChatClient(
                "{\"destinationAccount\":\"DE89370400440532013000\",\"amount\":250.00,\"currency\":\"EUR\",\"tenantId\":\"tenant-b\"}"));
            var factory = ApiFactory.Create(_factory, scenario);
            var sessionId = Guid.NewGuid();

            var response = await ApiFactory.CreateClient(factory, ApiFactory.UserKeyA).PostAsJsonAsync(
                "/api/agent/invoke",
                new { SessionId = sessionId, Prompt = FakeChatClient.LowValueIntent });

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("SchemaViolation", body);
            Assert.DoesNotContain("Exception", body);
            Assert.Null(await scenario.Repository.GetAsync(TransferScenario.TenantA, sessionId));
            Assert.Equal(0, scenario.Ledger.ExecutedCount);
        }

        [Fact]
        public async Task InsufficientFunds_Returns422WithDomainCode()
        {
            var scenario = new TransferScenario(FakeChatClient.HighValueTransfer());
            scenario.Ledger.SeedAccount(TransferScenario.TenantA, TransferScenario.OperatingAccount, "EUR", 100m);
            var factory = ApiFactory.Create(_factory, scenario);

            var response = await ApiFactory.CreateClient(factory, ApiFactory.UserKeyA).PostAsJsonAsync(
                "/api/agent/invoke",
                new { SessionId = Guid.NewGuid(), Prompt = FakeChatClient.HighValueIntent });

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.Contains("InsufficientFunds", await response.Content.ReadAsStringAsync());
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
