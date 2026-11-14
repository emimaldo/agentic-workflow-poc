using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Api.Development;
using AgenticWorkflowPoC.Api.Models;
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

            Assert.IsType<RoutingOutcome.Suspended>(response);
            var session = await scenario.Repository.GetAsync(TenantA, sessionId);
            Assert.Equal(HitlSessionStatus.PendingReview, session?.Status);
            Assert.Equal(TenantA, session?.TenantId);
            Assert.Contains(FakeChatClient.DestinationIban, session?.SerializedContext);
            Assert.Equal(0, scenario.Ledger.ExecutedCount);
        }

        [Fact]
        public async Task InvokeAsync_BelowThreshold_Completes()
        {
            var scenario = new TransferScenario(FakeChatClient.LowValueTransfer());

            var response = await scenario.CreateAgentService().InvokeAsync(Guid.NewGuid(), FakeChatClient.LowValueIntent);

            Assert.IsType<RoutingOutcome.Completed>(response);
            Assert.Equal(1, scenario.Ledger.ExecutedCount);
        }

        [Fact]
        public async Task InvokeAsync_WhenModelReturnsUnknownField_DoesNotPersistOrExecute()
        {
            var scenario = new TransferScenario(new FakeChatClient(
                "{\"destinationAccount\":\"DE89370400440532013000\",\"amount\":250.00,\"currency\":\"EUR\",\"tool\":\"delete\"}"));
            var sessionId = Guid.NewGuid();

            var response = await scenario.CreateAgentService().InvokeAsync(sessionId, FakeChatClient.LowValueIntent);

            Assert.Equal("SchemaViolation", Assert.Single(Assert.IsType<RoutingOutcome.Rejected>(response).Errors).Code);
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

            Assert.Equal("InsufficientFunds", Assert.Single(Assert.IsType<RoutingOutcome.Rejected>(response).Errors).Code);
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

            Assert.IsType<ResumeOutcome.Queued>(response);
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

            Assert.IsType<ResumeOutcome.RejectedByOperator>(response);
            Assert.Equal(HitlSessionStatus.Rejected, (await scenario.Repository.GetAsync(TenantA, sessionId))?.Status);
            Assert.False(await scenario.CreateProcessor().ProcessNextAsync());
            Assert.Equal(0, scenario.Chat.CallCount);
            Assert.Equal(0, scenario.Ledger.ExecutedCount);
        }

        [Fact]
        public async Task ResumeAsync_WhenSessionDoesNotExist_ReturnsNotFound()
        {
            var response = await new TransferScenario().CreateAgentService().ResumeAsync(Guid.NewGuid(), true);

            Assert.IsType<ResumeOutcome.NotFound>(response);
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

            Assert.Equal(HitlSessionStatus.Rejected, Assert.IsType<ResumeOutcome.AlreadyResolved>(secondResponse).CurrentStatus);
        }

        [Fact]
        public async Task ResumeAsync_WhenCalledFromAnotherTenant_ReturnsNotFound()
        {
            var scenario = new TransferScenario(tenantId: TransferScenario.TenantB);
            var sessionId = Guid.NewGuid();
            await scenario.Repository.CreatePendingAsync(scenario.PendingSession(sessionId, tenantId: TenantA));

            var response = await scenario.CreateAgentService().ResumeAsync(sessionId, true);

            Assert.IsType<ResumeOutcome.NotFound>(response);
            Assert.Equal(HitlSessionStatus.PendingReview, (await scenario.Repository.GetAsync(TenantA, sessionId))?.Status);
        }

        [Fact]
        public async Task InvokeAsync_PersistsTheValidatedCommandAsImmutableContext()
        {
            var scenario = new TransferScenario(FakeChatClient.HighValueTransfer());
            var sessionId = Guid.NewGuid();

            await scenario.CreateAgentService().InvokeAsync(sessionId, FakeChatClient.HighValueIntent);

            var session = await scenario.Repository.GetAsync(TenantA, sessionId);
            var context = JsonSerializer.Deserialize<EnrichedTransferCommand>(session!.SerializedContext);
            Assert.Equal(TransferScenario.Command(12_500m), context);
            Assert.Equal(context, Assert.IsType<EnrichedTransferCommand>(scenario.Hitl.Context));
        }

        [Fact]
        public async Task ResumeAsync_RecordsOperatorAndTimestamp()
        {
            var scenario = new TransferScenario();
            scenario.Tenant.SubjectId = "ops-admin-7";
            var sessionId = Guid.NewGuid();
            await scenario.Repository.CreatePendingAsync(scenario.PendingSession(sessionId));

            await scenario.CreateAgentService().ResumeAsync(sessionId, true);

            var session = await scenario.Repository.GetAsync(TenantA, sessionId);
            Assert.Equal("ops-admin-7", session?.ResolvedBy);
            Assert.Equal(scenario.Time.GetUtcNow(), session?.ResolvedAt);
        }

        [Fact]
        public async Task ProcessNextAsync_WhenContextTenantDiffersFromSessionTenant_RefusesToExecute()
        {
            var scenario = new TransferScenario();
            var sessionId = Guid.NewGuid();
            var tampered = scenario.PendingSession(sessionId, status: HitlSessionStatus.Approved) with
            {
                SerializedContext = JsonSerializer.Serialize(TransferScenario.Command(12_500m, tenantId: TransferScenario.TenantB))
            };
            await scenario.Repository.CreatePendingAsync(tampered);

            Assert.True(await scenario.CreateProcessor().ProcessNextAsync());

            Assert.Equal(HitlSessionStatus.Failed, (await scenario.Repository.GetAsync(TenantA, sessionId))?.Status);
            Assert.Equal(0, scenario.Ledger.ExecutedCount);
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

            // Attempt 1 fails: no hot retry while the processing lease is still active.
            Assert.True(await processor.ProcessNextAsync());
            Assert.False(await processor.ProcessNextAsync());
            Assert.Equal(1, executor.CallCount);
            Assert.Equal(HitlSessionStatus.Processing, (await scenario.Repository.GetAsync(TenantA, sessionId))?.Status);

            // Each retry happens only after the lease expires (backoff), up to three attempts.
            scenario.Time.Now += TimeSpan.FromMinutes(3);
            Assert.True(await processor.ProcessNextAsync());
            scenario.Time.Now += TimeSpan.FromMinutes(3);
            Assert.True(await processor.ProcessNextAsync());

            Assert.Equal(3, executor.CallCount);
            Assert.Equal(HitlSessionStatus.Failed, (await scenario.Repository.GetAsync(TenantA, sessionId))?.Status);
        }

        [Fact]
        public async Task ProcessNextAsync_WhenTransientFailureClears_RetrySucceedsAfterBackoff()
        {
            var scenario = new TransferScenario();
            var sessionId = Guid.NewGuid();
            await scenario.Repository.CreatePendingAsync(scenario.PendingSession(sessionId, status: HitlSessionStatus.Approved));
            var flaky = new FlakyTransferExecutor(scenario.Ledger, failures: 1);
            var processor = scenario.CreateProcessor(flaky);

            Assert.True(await processor.ProcessNextAsync());
            scenario.Time.Now += TimeSpan.FromMinutes(3);
            Assert.True(await processor.ProcessNextAsync());

            Assert.Equal(HitlSessionStatus.Completed, (await scenario.Repository.GetAsync(TenantA, sessionId))?.Status);
            Assert.Equal(1, scenario.Ledger.ExecutedCount);
        }

        private sealed class FlakyTransferExecutor(ITransferExecutor inner, int failures) : ITransferExecutor
        {
            private int _remainingFailures = failures;

            public Task<TransferExecutionStatus> ExecuteAsync(TransferExecutionRequest request, CancellationToken cancellationToken = default) =>
                _remainingFailures-- > 0
                    ? throw new InvalidOperationException("Simulated transient SQL failure.")
                    : inner.ExecuteAsync(request, cancellationToken);
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
