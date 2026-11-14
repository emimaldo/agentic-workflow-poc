using System;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Api.Services;
using AgenticWorkflowPoC.Tests.Support;
using Xunit;

namespace AgenticWorkflowPoC.Tests.Services
{
    public class ResumeSignalTests
    {
        [Fact]
        public async Task ResumeAsync_WhenApproved_PersistsFirstThenEmitsExactlyOneSignal()
        {
            var scenario = new TransferScenario();
            var sessionId = Guid.NewGuid();
            await scenario.Repository.CreatePendingAsync(scenario.PendingSession(sessionId));

            await scenario.CreateAgentService().ResumeAsync(sessionId, true);

            Assert.True(scenario.ResumeSignals.Reader.TryRead(out var signal));
            Assert.Equal(new ResumedWorkflowEvent(TransferScenario.TenantA, sessionId), signal);
            Assert.False(scenario.ResumeSignals.Reader.TryRead(out _));
        }

        [Fact]
        public async Task ResumeAsync_WhenRejectedOrNotFound_EmitsNoSignal()
        {
            var scenario = new TransferScenario();
            var sessionId = Guid.NewGuid();
            await scenario.Repository.CreatePendingAsync(scenario.PendingSession(sessionId));
            var service = scenario.CreateAgentService();

            await service.ResumeAsync(sessionId, false);
            await service.ResumeAsync(Guid.NewGuid(), true);

            Assert.False(scenario.ResumeSignals.Reader.TryRead(out _));
        }

        [Fact]
        public async Task Channel_WhenFull_AppliesBackpressureInsteadOfDropping()
        {
            var channel = ResumeSignalChannel.Create(capacity: 1);
            await channel.Writer.WriteAsync(new ResumedWorkflowEvent("tenant-a", Guid.NewGuid()));

            var pendingWrite = channel.Writer.WriteAsync(new ResumedWorkflowEvent("tenant-a", Guid.NewGuid())).AsTask();
            await Task.Delay(50);
            Assert.False(pendingWrite.IsCompleted);

            Assert.True(channel.Reader.TryRead(out _));
            await pendingWrite.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.True(channel.Reader.TryRead(out _));
        }
    }
}
