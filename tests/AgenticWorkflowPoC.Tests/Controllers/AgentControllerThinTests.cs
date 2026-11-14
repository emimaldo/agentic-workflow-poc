using System;
using System.Threading;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Api.Controllers;
using AgenticWorkflowPoC.Api.Models;
using AgenticWorkflowPoC.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace AgenticWorkflowPoC.Tests.Controllers
{
    public class AgentControllerThinTests
    {
        [Fact]
        public async Task InvokeAgent_DelegatesToService_ReturnsAcceptedWhenSuspended()
        {
            var fake = new FakeAgentService(invoke: new RoutingOutcome.Suspended(Guid.NewGuid(), "reason"));
            var controller = new AgentController(fake);

            var res = await controller.InvokeAgent(new AgentRequest(Guid.NewGuid(), "prompt"), CancellationToken.None);

            Assert.IsType<AcceptedResult>(res);
        }

        [Fact]
        public async Task ResumeAgent_DelegatesToService_ReturnsAcceptedWhenQueued()
        {
            var fake = new FakeAgentService(resume: new ResumeOutcome.Queued(Guid.NewGuid()));
            var controller = new AgentController(fake);

            var res = await controller.ResumeAgent(Guid.NewGuid(), new ResumeRequest(true), CancellationToken.None);

            Assert.IsType<AcceptedResult>(res);
        }

        [Fact]
        public async Task InvokeAgent_DelegatesToService_Returns422ProblemWhenRejected()
        {
            var fake = new FakeAgentService(invoke: new RoutingOutcome.Rejected(
                Guid.NewGuid(),
                [new RoutingError("Ungrounded", "The amount does not appear in the request.")]));
            var controller = new AgentController(fake);

            var res = await controller.InvokeAgent(new AgentRequest(Guid.NewGuid(), "prompt"), CancellationToken.None);

            var result = Assert.IsType<ObjectResult>(res);
            Assert.Equal(422, result.StatusCode);
            Assert.IsType<ProblemDetails>(result.Value);
        }

        [Fact]
        public async Task InvokeAgent_WithEmptySession_ReturnsValidationProblemWithoutCallingService()
        {
            var fake = new FakeAgentService();
            var controller = new AgentController(fake);

            var res = await controller.InvokeAgent(new AgentRequest(Guid.Empty, "prompt"), CancellationToken.None);

            Assert.IsType<ObjectResult>(res);
            Assert.False(fake.Called);
        }

        private sealed class FakeAgentService(RoutingOutcome? invoke = null, ResumeOutcome? resume = null) : IAgentService
        {
            public bool Called { get; private set; }

            public Task<RoutingOutcome> InvokeAsync(Guid sessionId, string prompt, CancellationToken cancellationToken = default)
            {
                Called = true;
                return Task.FromResult(invoke ?? throw new InvalidOperationException());
            }

            public Task<ResumeOutcome> ResumeAsync(Guid sessionId, bool isApproved, CancellationToken cancellationToken = default)
            {
                Called = true;
                return Task.FromResult(resume ?? throw new InvalidOperationException());
            }
        }
    }
}
