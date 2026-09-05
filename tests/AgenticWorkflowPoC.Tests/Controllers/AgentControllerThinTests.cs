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
            var fake = new FakeAgentService(new AgentResponse("Suspended", "reason", "s1"));
            var controller = new AgentController(fake, NullLogger<AgentController>.Instance);

            var req = new AgentRequest("s1", "prompt");
            var res = await controller.InvokeAgent(req);

            Assert.IsType<AcceptedResult>(res);
        }

        [Fact]
        public async Task ResumeAgent_DelegatesToService_ReturnsOk()
        {
            var fake = new FakeAgentService(new AgentResponse("Completed", Response: "ok"));
            var controller = new AgentController(fake, NullLogger<AgentController>.Instance);

            var res = await controller.ResumeAgent("s1", new ResumeRequest(true));

            Assert.IsType<OkObjectResult>(res);
        }

        private class FakeAgentService : IAgentService
        {
            private readonly AgentResponse _resp;
            public FakeAgentService(AgentResponse resp) => _resp = resp;
            public Task<AgentResponse> InvokeAsync(string sessionId, string prompt) => Task.FromResult(_resp);
            public Task<AgentResponse> ResumeAsync(string sessionId, bool isApproved) => Task.FromResult(_resp);
        }
    }
}
