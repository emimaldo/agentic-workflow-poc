using System.Collections.Generic;
using System;
using System.Threading;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Api.Development;
using AgenticWorkflowPoC.Api.Controllers;
using AgenticWorkflowPoC.Api.Models;
using AgenticWorkflowPoC.Api.Services;
using AgenticWorkflowPoC.Core.Entities;
using AgenticWorkflowPoC.Plugins;
using AgenticWorkflowPoC.Plugins.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
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
            var fakeChat = new FakeChatClient(returnConflict: true);
            var plugin = new StaffOverridesPlugin(NullLogger<StaffOverridesPlugin>.Instance, hitl);
            var agent = fakeChat.AsAIAgent(instructions: "Extract staffId and date as JSON.");
            var agentService = new AgentService(
                agent,
                hitl,
                NullLogger<AgentService>.Instance,
                plugin,
                new ExtractionValidator(),
                new InMemoryHitlSessionRepository());

            var controller = new AgentController(agentService, NullLogger<AgentController>.Instance);
            var sessionId = Guid.NewGuid();
            var req = new AgentRequest(sessionId, "Please change EMP-REQ-001 availability to 2026-09-01T09:00:00Z");

            // Act
            var result = await controller.InvokeAgent(req, CancellationToken.None);

            // Assert
            var accepted = Assert.IsType<Microsoft.AspNetCore.Mvc.AcceptedResult>(result);
            // ensure HITL state was set
            Assert.True(hitl.IsSuspended);
            Assert.False(string.IsNullOrWhiteSpace(hitl.Reason));
        }
    }

}
