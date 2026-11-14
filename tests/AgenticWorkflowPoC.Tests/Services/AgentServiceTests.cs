using System;
using System.Text.Json;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Api.Development;
using AgenticWorkflowPoC.Api.Models;
using AgenticWorkflowPoC.Api.Services;
using AgenticWorkflowPoC.Core.Entities;
using AgenticWorkflowPoC.Plugins;
using AgenticWorkflowPoC.Plugins.Operations;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.AI;
using Xunit;

namespace AgenticWorkflowPoC.Tests.Services
{
    public class AgentServiceTests
    {
        [Fact]
        public async Task InvokeAsync_WhenModelReturnsConflict_ServiceReturnsSuspended()
        {
            var hitl = new HitlStateService();
            var fakeChat = new FakeChatClient(returnConflict: true);
            var repository = new InMemoryHitlSessionRepository();
            var sessionId = Guid.NewGuid();
            var agentService = CreateAgentService(fakeChat, hitl, repository);

            var resp = await agentService.InvokeAsync(sessionId, "tenant-a", "prompt");

            Assert.Equal("Suspended", resp.Status);
            Assert.False(string.IsNullOrWhiteSpace(resp.Message));
            var session = await repository.GetAsync("tenant-a", sessionId);
            Assert.Equal(HitlSessionStatus.PendingReview, session?.Status);
            Assert.Contains("EMP-REQ-001", session?.PendingActionPayload);
            Assert.Equal("tenant-a", session?.TenantId);
        }

        [Fact]
        public async Task InvokeAsync_WhenModelReturnsOk_ServiceReturnsCompleted()
        {
            var hitl = new HitlStateService();
            var fakeChat = new FakeChatClient(returnConflict: false);
            var agentService = CreateAgentService(fakeChat, hitl, new InMemoryHitlSessionRepository());

            var resp = await agentService.InvokeAsync(Guid.NewGuid(), "tenant-a", "prompt");

            Assert.Equal("Completed", resp.Status);
            Assert.Contains("SUCCESS", resp.Response);
        }

        [Fact]
        public async Task ResumeAsync_WhenApproved_QueuesActionForBackgroundExecution()
        {
            var hitl = new HitlStateService();
            var fakeChat = new FakeChatClient(returnConflict: false);
            var repository = new InMemoryHitlSessionRepository();
            var executor = new InMemoryStaffAvailabilityExecutor();
            var sessionId = Guid.NewGuid();
            await repository.CreatePendingAsync(CreatePendingSession(sessionId, "tenant-a"));
            var agentService = CreateAgentService(fakeChat, hitl, repository, executor);

            var resp = await agentService.ResumeAsync(sessionId, "tenant-a", true);

            Assert.Equal("Queued", resp.Status);
            Assert.Equal(HitlSessionStatus.Approved, (await repository.GetAsync("tenant-a", sessionId))?.Status);
            Assert.Equal(0, executor.ExecutionCount);

            var processor = new HitlWorkProcessor(
                repository,
                CreatePlugin(hitl, executor),
                NullLogger<HitlWorkProcessor>.Instance);
            Assert.True(await processor.ProcessNextAsync());

            Assert.Equal(HitlSessionStatus.Completed, (await repository.GetAsync("tenant-a", sessionId))?.Status);
            Assert.Equal(1, executor.ExecutionCount);
            Assert.Equal(0, fakeChat.CallCount);
        }

        [Fact]
        public async Task ResumeAsync_WhenRejected_RejectsWithoutCallingModel()
        {
            var fakeChat = new FakeChatClient(returnConflict: false);
            var repository = new InMemoryHitlSessionRepository();
            var sessionId = Guid.NewGuid();
            await repository.CreatePendingAsync(CreatePendingSession(sessionId, "tenant-a"));
            var agentService = CreateAgentService(fakeChat, new HitlStateService(), repository);

            var response = await agentService.ResumeAsync(sessionId, "tenant-a", false);

            Assert.Equal("Rejected", response.Status);
            Assert.Equal(HitlSessionStatus.Rejected, (await repository.GetAsync("tenant-a", sessionId))?.Status);
            Assert.Equal(0, fakeChat.CallCount);
        }

        [Fact]
        public async Task ResumeAsync_WhenSessionDoesNotExist_ReturnsNotFound()
        {
            var agentService = CreateAgentService(
                new FakeChatClient(returnConflict: false),
                new HitlStateService(),
                new InMemoryHitlSessionRepository());

            var response = await agentService.ResumeAsync(Guid.NewGuid(), "tenant-a", true);

            Assert.Equal("NotFound", response.Status);
        }

        [Fact]
        public async Task ResumeAsync_WhenSessionAlreadyResolved_ReturnsConflict()
        {
            var repository = new InMemoryHitlSessionRepository();
            var sessionId = Guid.NewGuid();
            await repository.CreatePendingAsync(CreatePendingSession(sessionId, "tenant-a"));
            var agentService = CreateAgentService(
                new FakeChatClient(returnConflict: false),
                new HitlStateService(),
                repository);

            await agentService.ResumeAsync(sessionId, "tenant-a", false);
            var secondResponse = await agentService.ResumeAsync(sessionId, "tenant-a", true);

            Assert.Equal("Conflict", secondResponse.Status);
        }

        private static AgentService CreateAgentService(
            FakeChatClient fakeChat,
            IHitlState hitl,
            InMemoryHitlSessionRepository repository,
            InMemoryStaffAvailabilityExecutor? executor = null)
        {
            executor ??= new InMemoryStaffAvailabilityExecutor();
            var agent = fakeChat.AsAIAgent(instructions: "Extract staffId and date as JSON.");
            return new AgentService(
                agent,
                hitl,
                NullLogger<AgentService>.Instance,
                CreatePlugin(hitl, executor),
                new ExtractionValidator(),
                repository);
        }

        private static StaffOverridesPlugin CreatePlugin(
            IHitlState hitl,
            InMemoryStaffAvailabilityExecutor executor) =>
            new(NullLogger<StaffOverridesPlugin>.Instance, hitl, executor);

        private static HitlSession CreatePendingSession(Guid sessionId, string tenantId)
        {
            var payload = JsonSerializer.Serialize(new PendingStaffOverride(
                "EMP-REQ-001",
                DateTimeOffset.Parse("2026-09-01T09:00:00Z")));

            return new HitlSession(
                sessionId,
                tenantId,
                "Requires human review.",
                payload,
                HitlSessionStatus.PendingReview,
                DateTimeOffset.UtcNow);
        }

        [Fact]
        public async Task ResumeAsync_WhenCalledFromAnotherTenant_ReturnsNotFound()
        {
            var repository = new InMemoryHitlSessionRepository();
            var sessionId = Guid.NewGuid();
            await repository.CreatePendingAsync(CreatePendingSession(sessionId, "tenant-a"));
            var service = CreateAgentService(
                new FakeChatClient(returnConflict: false),
                new HitlStateService(),
                repository);

            var response = await service.ResumeAsync(sessionId, "tenant-b", true);

            Assert.Equal("NotFound", response.Status);
            Assert.Equal(HitlSessionStatus.PendingReview, (await repository.GetAsync("tenant-a", sessionId))?.Status);
        }

        [Fact]
        public async Task InvokeAsync_WhenModelReturnsUnknownField_DoesNotPersistOrExecute()
        {
            var fakeChat = new FakeChatClient(
                "{\"staffId\":\"EMP-102\",\"date\":\"2026-09-01T09:00:00Z\",\"tool\":\"delete\"}");
            var repository = new InMemoryHitlSessionRepository();
            var executor = new InMemoryStaffAvailabilityExecutor();
            var service = CreateAgentService(fakeChat, new HitlStateService(), repository, executor);
            var sessionId = Guid.NewGuid();

            var response = await service.InvokeAsync(sessionId, "tenant-a", "prompt");

            Assert.Equal("Error", response.Status);
            Assert.Null(await repository.GetAsync("tenant-a", sessionId));
            Assert.Equal(0, executor.ExecutionCount);
        }

        [Fact]
        public async Task ProcessNextAsync_WhenLeaseExpiresAfterEffect_ReclaimsWorkIdempotently()
        {
            var repository = new InMemoryHitlSessionRepository();
            var executor = new InMemoryStaffAvailabilityExecutor();
            var sessionId = Guid.NewGuid();
            await repository.CreatePendingAsync(CreatePendingSession(sessionId, "tenant-a") with
            {
                Status = HitlSessionStatus.Approved
            });

            var staleClaim = await repository.TryClaimNextApprovedAsync(
                DateTimeOffset.UtcNow.AddMinutes(-3),
                TimeSpan.FromMinutes(2));
            Assert.Equal(HitlSessionStatus.Processing, staleClaim?.Status);

            await executor.ExecuteAsync(
                sessionId,
                "tenant-a",
                "EMP-REQ-001",
                DateTimeOffset.Parse("2026-09-01T09:00:00Z"));
            Assert.Equal(1, executor.ExecutionCount);

            var plugin = CreatePlugin(new HitlStateService(), executor);
            var processor = new HitlWorkProcessor(repository, plugin, NullLogger<HitlWorkProcessor>.Instance);
            Assert.True(await processor.ProcessNextAsync());
            Assert.Equal(HitlSessionStatus.Completed, (await repository.GetAsync("tenant-a", sessionId))?.Status);
            Assert.Equal(1, executor.ExecutionCount);
        }

        [Fact]
        public async Task TryClaimNextApprovedAsync_ConcurrentWorkersOnlyClaimOneSession()
        {
            var repository = new InMemoryHitlSessionRepository();
            var sessionId = Guid.NewGuid();
            await repository.CreatePendingAsync(CreatePendingSession(sessionId, "tenant-a") with
            {
                Status = HitlSessionStatus.Approved
            });

            var now = DateTimeOffset.UtcNow;
            var claims = await Task.WhenAll(
                repository.TryClaimNextApprovedAsync(now, TimeSpan.FromMinutes(2)),
                repository.TryClaimNextApprovedAsync(now, TimeSpan.FromMinutes(2)));

            Assert.Single(claims, claim => claim is not null);
        }

        [Fact]
        public async Task ProcessNextAsync_WhenExecutionKeepsFailing_StopsAfterThreeAttempts()
        {
            var repository = new InMemoryHitlSessionRepository();
            var sessionId = Guid.NewGuid();
            await repository.CreatePendingAsync(CreatePendingSession(sessionId, "tenant-a") with
            {
                Status = HitlSessionStatus.Approved
            });
            var executor = new FailingAvailabilityExecutor();
            var plugin = new StaffOverridesPlugin(
                NullLogger<StaffOverridesPlugin>.Instance,
                new HitlStateService(),
                executor);
            var processor = new HitlWorkProcessor(repository, plugin, NullLogger<HitlWorkProcessor>.Instance);

            Assert.True(await processor.ProcessNextAsync());
            Assert.True(await processor.ProcessNextAsync());
            Assert.True(await processor.ProcessNextAsync());

            Assert.Equal(3, executor.CallCount);
            Assert.Equal(HitlSessionStatus.Failed, (await repository.GetAsync("tenant-a", sessionId))?.Status);
        }

        private sealed class FailingAvailabilityExecutor : IStaffAvailabilityExecutor
        {
            public int CallCount { get; private set; }

            public Task ExecuteAsync(
                Guid operationId,
                string tenantId,
                string staffId,
                DateTimeOffset newAvailability,
                System.Threading.CancellationToken cancellationToken = default)
            {
                CallCount++;
                throw new InvalidOperationException("Simulated execution failure.");
            }
        }
    }
}
