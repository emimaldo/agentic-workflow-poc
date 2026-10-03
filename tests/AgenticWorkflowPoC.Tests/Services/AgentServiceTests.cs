using System;
using System.Text.Json;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Api.Development;
using AgenticWorkflowPoC.Api.Models;
using AgenticWorkflowPoC.Api.Services;
using AgenticWorkflowPoC.Core.Entities;
using AgenticWorkflowPoC.Plugins;
using AgenticWorkflowPoC.Plugins.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Agents.AI;
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

            var resp = await agentService.InvokeAsync(sessionId, "prompt");

            Assert.Equal("Suspended", resp.Status);
            Assert.False(string.IsNullOrWhiteSpace(resp.Message));
            var session = await repository.GetAsync(sessionId);
            Assert.Equal(HitlSessionStatus.PendingReview, session?.Status);
            Assert.Contains("EMP-REQ-001", session?.PendingActionPayload);
        }

        [Fact]
        public async Task InvokeAsync_WhenModelReturnsOk_ServiceReturnsCompleted()
        {
            var hitl = new HitlStateService();
            var fakeChat = new FakeChatClient(returnConflict: false);
            var agentService = CreateAgentService(fakeChat, hitl, new InMemoryHitlSessionRepository());

            var resp = await agentService.InvokeAsync(Guid.NewGuid(), "prompt");

            Assert.Equal("Completed", resp.Status);
            Assert.Contains("SUCCESS", resp.Response);
        }

        [Fact]
        public async Task ResumeAsync_WhenApproved_ExecutesPendingActionAndCompletesSession()
        {
            var hitl = new HitlStateService();
            var fakeChat = new FakeChatClient(returnConflict: false);
            var repository = new InMemoryHitlSessionRepository();
            var sessionId = Guid.NewGuid();
            await repository.CreatePendingAsync(CreatePendingSession(sessionId));
            var agentService = CreateAgentService(fakeChat, hitl, repository);

            var resp = await agentService.ResumeAsync(sessionId, true);

            Assert.Equal("Completed", resp.Status);
            Assert.Contains("SUCCESS", resp.Response);
            Assert.Equal(HitlSessionStatus.Completed, (await repository.GetAsync(sessionId))?.Status);
            Assert.Equal(0, fakeChat.CallCount);
        }

        [Fact]
        public async Task ResumeAsync_WhenRejected_RejectsWithoutCallingModel()
        {
            var fakeChat = new FakeChatClient(returnConflict: false);
            var repository = new InMemoryHitlSessionRepository();
            var sessionId = Guid.NewGuid();
            await repository.CreatePendingAsync(CreatePendingSession(sessionId));
            var agentService = CreateAgentService(fakeChat, new HitlStateService(), repository);

            var response = await agentService.ResumeAsync(sessionId, false);

            Assert.Equal("Rejected", response.Status);
            Assert.Equal(HitlSessionStatus.Rejected, (await repository.GetAsync(sessionId))?.Status);
            Assert.Equal(0, fakeChat.CallCount);
        }

        [Fact]
        public async Task ResumeAsync_WhenSessionDoesNotExist_ReturnsNotFound()
        {
            var agentService = CreateAgentService(
                new FakeChatClient(returnConflict: false),
                new HitlStateService(),
                new InMemoryHitlSessionRepository());

            var response = await agentService.ResumeAsync(Guid.NewGuid(), true);

            Assert.Equal("NotFound", response.Status);
        }

        [Fact]
        public async Task ResumeAsync_WhenSessionAlreadyResolved_ReturnsConflict()
        {
            var repository = new InMemoryHitlSessionRepository();
            var sessionId = Guid.NewGuid();
            await repository.CreatePendingAsync(CreatePendingSession(sessionId));
            var agentService = CreateAgentService(
                new FakeChatClient(returnConflict: false),
                new HitlStateService(),
                repository);

            await agentService.ResumeAsync(sessionId, false);
            var secondResponse = await agentService.ResumeAsync(sessionId, true);

            Assert.Equal("Conflict", secondResponse.Status);
        }

        private static AgentService CreateAgentService(
            FakeChatClient fakeChat,
            IHitlState hitl,
            InMemoryHitlSessionRepository repository)
        {
            var plugin = new StaffOverridesPlugin(NullLogger<StaffOverridesPlugin>.Instance, hitl);
            var agent = fakeChat.AsAIAgent(instructions: "Extract staffId and date as JSON.");
            return new AgentService(
                agent,
                hitl,
                NullLogger<AgentService>.Instance,
                plugin,
                new ExtractionValidator(),
                repository);
        }

        private static HitlSession CreatePendingSession(Guid sessionId)
        {
            var payload = JsonSerializer.Serialize(new PendingStaffOverride(
                "EMP-REQ-001",
                DateTimeOffset.Parse("2026-09-01T09:00:00Z")));

            return new HitlSession(
                sessionId,
                "Requires human review.",
                payload,
                HitlSessionStatus.PendingReview,
                DateTimeOffset.UtcNow);
        }
    }
}
