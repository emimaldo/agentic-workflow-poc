using System;
using System.Threading;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Api.Development;
using AgenticWorkflowPoC.Core.Entities;
using AgenticWorkflowPoC.Plugins.Financial;
using AgenticWorkflowPoC.Tests.Support;
using Xunit;

namespace AgenticWorkflowPoC.Tests.Services
{
    public class AgentServiceTests
    {
        private const string TenantA = TransferScenario.TenantA;

        [Fact]
        public async Task InvokeAsync_AboveThreshold_PersistsSuspendedContextAndReturnsSuspended()
        {
            var scenario = new TransferScenario(FakeChatClient.HighValueTransfer());
            var sessionId = Guid.NewGuid();

            var response = await scenario.CreateAgentService().InvokeAsync(sessionId, FakeChatClient.HighValueIntent);

            Assert.Equal("Suspended", response.Status);
            var session = await scenario.Repository.GetAsync(TenantA, sessionId);
            Assert.Equal(HitlSessionStatus.PendingReview, session?.Status);
            Assert.Equal(TenantA, session?.TenantId);
            Assert.Contains(FakeChatClient.DestinationIban, session?.PendingActionPayload);
            Assert.Equal(0, scenario.Ledger.ExecutedCount);
        }

        [Fact]
        public async Task InvokeAsync_BelowThreshold_Completes()
        {
            var scenario = new TransferScenario(FakeChatClient.LowValueTransfer());

            var response = await scenario.CreateAgentService().InvokeAsync(Guid.NewGuid(), FakeChatClient.LowValueIntent);

            Assert.Equal("Completed", response.Status);
            Assert.Equal(1, scenario.Ledger.ExecutedCount);
        }

        [Fact]
        public async Task InvokeAsync_WhenModelReturnsUnknownField_DoesNotPersistOrExecute()
        {
            var scenario = new TransferScenario(new FakeChatClient(
                "{\"destinationAccount\":\"DE89370400440532013000\",\"amount\":250.00,\"currency\":\"EUR\",\"tool\":\"delete\"}"));
            var sessionId = Guid.NewGuid();

            var response = await scenario.CreateAgentService().InvokeAsync(sessionId, FakeChatClient.LowValueIntent);

            Assert.Equal("Error", response.Status);
            Assert.Null(await scenario.Repository.GetAsync(TenantA, sessionId));
            Assert.Equal(0, scenario.Ledger.ExecutedCount);
        }

        [Fact]
        public async Task InvokeAsync_DomainRejection_DoesNotPersistOrExecute()
        {
            var scenario = new TransferScenario(FakeChatClient.HighValueTransfer());
            scenario.Ledger.SeedAccount(TenantA, TransferScenario.OperatingAccount, "EUR", 100m);
            var sessionId = Guid.NewGuid();

            var response = await scenario.CreateAgentService().InvokeAsync(sessionId, FakeChatClient.HighValueIntent);

            Assert.Equal("Error", response.Status);
            Assert.Null(await scenario.Repository.GetAsync(TenantA, sessionId));
            Assert.Equal(0, scenario.Ledger.ExecutedCount);
        }

        [Fact]
        public async Task ResumeAsync_WhenApproved_QueuesAndWorkerExecutesWithoutCallingModel()
        {
            var scenario = new TransferScenario();
            var sessionId = Guid.NewGuid();
            await scenario.Repository.CreatePendingAsync(scenario.PendingSession(sessionId));

            var response = await scenario.CreateAgentService().ResumeAsync(sessionId, true);

            Assert.Equal("Queued", response.Status);
            Assert.Equal(HitlSessionStatus.Approved, (await scenario.Repository.GetAsync(TenantA, sessionId))?.Status);
            Assert.Equal(0, scenario.Ledger.ExecutedCount);

            Assert.True(await scenario.CreateProcessor().ProcessNextAsync());

            Assert.Equal(HitlSessionStatus.Completed, (await scenario.Repository.GetAsync(TenantA, sessionId))?.Status);
            Assert.Equal(TransferScenario.OpeningBalance - 12_500m, scenario.Ledger.GetBalance(TenantA, TransferScenario.OperatingAccount));
            Assert.Equal(0, scenario.Chat.CallCount);
        }

        [Fact]
        public async Task ResumeAsync_WhenRejected_RejectsWithoutCallingModelOrExecuting()
        {
            var scenario = new TransferScenario();
            var sessionId = Guid.NewGuid();
            await scenario.Repository.CreatePendingAsync(scenario.PendingSession(sessionId));

            var response = await scenario.CreateAgentService().ResumeAsync(sessionId, false);

            Assert.Equal("Rejected", response.Status);
            Assert.Equal(HitlSessionStatus.Rejected, (await scenario.Repository.GetAsync(TenantA, sessionId))?.Status);
            Assert.False(await scenario.CreateProcessor().ProcessNextAsync());
            Assert.Equal(0, scenario.Chat.CallCount);
            Assert.Equal(0, scenario.Ledger.ExecutedCount);
        }

        [Fact]
        public async Task ResumeAsync_WhenSessionDoesNotExist_ReturnsNotFound()
        {
            var response = await new TransferScenario().CreateAgentService().ResumeAsync(Guid.NewGuid(), true);

            Assert.Equal("NotFound", response.Status);
        }

        [Fact]
        public async Task ResumeAsync_WhenSessionAlreadyResolved_ReturnsConflict()
        {
            var scenario = new TransferScenario();
            var sessionId = Guid.NewGuid();
            await scenario.Repository.CreatePendingAsync(scenario.PendingSession(sessionId));
            var service = scenario.CreateAgentService();

            await service.ResumeAsync(sessionId, false);
            var secondResponse = await service.ResumeAsync(sessionId, true);

            Assert.Equal("Conflict", secondResponse.Status);
        }

        [Fact]
        public async Task ResumeAsync_WhenCalledFromAnotherTenant_ReturnsNotFound()
        {
            var scenario = new TransferScenario(tenantId: TransferScenario.TenantB);
            var sessionId = Guid.NewGuid();
            await scenario.Repository.CreatePendingAsync(scenario.PendingSession(sessionId, tenantId: TenantA));

            var response = await scenario.CreateAgentService().ResumeAsync(sessionId, true);

            Assert.Equal("NotFound", response.Status);
            Assert.Equal(HitlSessionStatus.PendingReview, (await scenario.Repository.GetAsync(TenantA, sessionId))?.Status);
        }

        [Fact]
        public async Task ProcessNextAsync_WhenLeaseExpiresAfterEffect_ReclaimsWorkIdempotently()
        {
            var scenario = new TransferScenario();
            var sessionId = Guid.NewGuid();
            await scenario.Repository.CreatePendingAsync(scenario.PendingSession(sessionId, status: HitlSessionStatus.Approved));

            // A previous worker claimed the session, debited the account, and crashed before completing it.
            var staleClaim = await scenario.Repository.TryClaimNextApprovedAsync(
                scenario.Time.GetUtcNow().AddMinutes(-3),
                TimeSpan.FromMinutes(2));
            Assert.Equal(HitlSessionStatus.Processing, staleClaim?.Status);
            await scenario.Ledger.ExecuteAsync(new TransferExecutionRequest(
                sessionId,
                TransferScenario.Command(12_500m),
                scenario.PolicyA.DailyLimit,
                scenario.Time.GetUtcNow()));

            Assert.True(await scenario.CreateProcessor().ProcessNextAsync());

            Assert.Equal(HitlSessionStatus.Completed, (await scenario.Repository.GetAsync(TenantA, sessionId))?.Status);
            Assert.Equal(1, scenario.Ledger.ExecutedCount);
            Assert.Equal(TransferScenario.OpeningBalance - 12_500m, scenario.Ledger.GetBalance(TenantA, TransferScenario.OperatingAccount));
        }

        [Fact]
        public async Task TryClaimNextApprovedAsync_ConcurrentWorkersOnlyClaimOneSession()
        {
            var scenario = new TransferScenario();
            await scenario.Repository.CreatePendingAsync(scenario.PendingSession(Guid.NewGuid(), status: HitlSessionStatus.Approved));

            var now = scenario.Time.GetUtcNow();
            var claims = await Task.WhenAll(
                scenario.Repository.TryClaimNextApprovedAsync(now, TimeSpan.FromMinutes(2)),
                scenario.Repository.TryClaimNextApprovedAsync(now, TimeSpan.FromMinutes(2)));

            Assert.Single(claims, claim => claim is not null);
        }

        [Fact]
        public async Task ProcessNextAsync_WhenExecutionKeepsFailing_StopsAfterThreeAttempts()
        {
            var scenario = new TransferScenario();
            var sessionId = Guid.NewGuid();
            await scenario.Repository.CreatePendingAsync(scenario.PendingSession(sessionId, status: HitlSessionStatus.Approved));
            var executor = new FailingTransferExecutor();
            var processor = scenario.CreateProcessor(executor);

            Assert.True(await processor.ProcessNextAsync());
            Assert.True(await processor.ProcessNextAsync());
            Assert.True(await processor.ProcessNextAsync());

            Assert.Equal(3, executor.CallCount);
            Assert.Equal(HitlSessionStatus.Failed, (await scenario.Repository.GetAsync(TenantA, sessionId))?.Status);
        }

        private sealed class FailingTransferExecutor : ITransferExecutor
        {
            public int CallCount { get; private set; }

            public Task<TransferExecutionStatus> ExecuteAsync(TransferExecutionRequest request, CancellationToken cancellationToken = default)
            {
                CallCount++;
                throw new InvalidOperationException("Simulated execution failure.");
            }
        }
    }
}
