using System;
using System.Threading;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Api.Controllers;
using AgenticWorkflowPoC.Api.Models;
using AgenticWorkflowPoC.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AgenticWorkflowPoC.Tests.Controllers
{
    public class AgentControllerThinTests
    {
        [Fact]
        public async Task InvokeAgent_DelegatesToService_ReturnsAcceptedWhenSuspended()
        {
            var fake = new FakeAgentService(new AgentResponse("Suspended", "reason", Guid.NewGuid().ToString()));
            var controller = new AgentController(fake, NullLogger<AgentController>.Instance);

            var req = new AgentRequest(Guid.NewGuid(), "prompt");
            var res = await controller.InvokeAgent(req, CancellationToken.None);

            Assert.IsType<AcceptedResult>(res);
        }

        [Fact]
        public async Task ResumeAgent_DelegatesToService_ReturnsOk()
        {
            var fake = new FakeAgentService(new AgentResponse("Completed", Response: "ok"));
            var controller = new AgentController(fake, NullLogger<AgentController>.Instance);

            var res = await controller.ResumeAgent(Guid.NewGuid(), new ResumeRequest(true), CancellationToken.None);

            Assert.IsType<OkObjectResult>(res);
        }

        private class FakeAgentService : IAgentService
        {
            private readonly AgentResponse _resp;
            public FakeAgentService(AgentResponse resp) => _resp = resp;
            public Task<AgentResponse> InvokeAsync(Guid sessionId, string prompt, CancellationToken cancellationToken = default) => Task.FromResult(_resp);
            public Task<AgentResponse> ResumeAsync(Guid sessionId, bool isApproved, CancellationToken cancellationToken = default) => Task.FromResult(_resp);
        }
    }
}
