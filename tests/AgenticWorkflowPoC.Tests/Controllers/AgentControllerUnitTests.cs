using System;
using System.Threading;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Api.Controllers;
using AgenticWorkflowPoC.Api.Development;
using AgenticWorkflowPoC.Api.Models;
using AgenticWorkflowPoC.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace AgenticWorkflowPoC.Tests.Controllers
{
    public class AgentControllerUnitTests
    {
        [Fact]
        public async Task InvokeAgent_WhenTransferExceedsThreshold_ReturnsAccepted()
        {
            var scenario = new TransferScenario(FakeChatClient.HighValueTransfer());
            var controller = new AgentController(scenario.CreateAgentService());

            var result = await controller.InvokeAgent(
                new AgentRequest(Guid.NewGuid(), FakeChatClient.HighValueIntent),
                CancellationToken.None);

            Assert.IsType<AcceptedResult>(result);
            Assert.True(scenario.Hitl.IsSuspended);
            Assert.False(string.IsNullOrWhiteSpace(scenario.Hitl.Reason));
        }
    }
}
