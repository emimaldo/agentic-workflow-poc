using System;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Claims;
using AgenticWorkflowPoC.Api.Controllers;
using AgenticWorkflowPoC.Api.Models;
using AgenticWorkflowPoC.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Http;
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
            controller.ControllerContext = CreateContext();

            var req = new AgentRequest(Guid.NewGuid(), "prompt");
            var res = await controller.InvokeAgent(req, CancellationToken.None);

            Assert.IsType<AcceptedResult>(res);
        }

        [Fact]
        public async Task ResumeAgent_DelegatesToService_ReturnsOk()
        {
            var fake = new FakeAgentService(new AgentResponse("Queued", Response: "ok"));
            var controller = new AgentController(fake, NullLogger<AgentController>.Instance);
            controller.ControllerContext = CreateContext();

            var res = await controller.ResumeAgent(Guid.NewGuid(), new ResumeRequest(true), CancellationToken.None);

            Assert.IsType<AcceptedResult>(res);
        }

        private static ControllerContext CreateContext() => new()
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim("tenant_id", "tenant-a"), new Claim(ClaimTypes.Role, "Admin")],
                    "Test"))
            }
        };

        private class FakeAgentService : IAgentService
        {
            private readonly AgentResponse _resp;
            public FakeAgentService(AgentResponse resp) => _resp = resp;
            public Task<AgentResponse> InvokeAsync(Guid sessionId, string tenantId, string prompt, CancellationToken cancellationToken = default) => Task.FromResult(_resp);
            public Task<AgentResponse> ResumeAsync(Guid sessionId, string tenantId, bool isApproved, CancellationToken cancellationToken = default) => Task.FromResult(_resp);
        }
    }
}
