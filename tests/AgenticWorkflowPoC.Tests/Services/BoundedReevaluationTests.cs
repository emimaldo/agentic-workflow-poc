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
    /// <summary>
    /// Chapter 3: an operator's approval is a hint, not an authorization. Between suspension and
    /// execution the domain may change; the worker must re-check it and refuse to execute.
    /// </summary>
    public class BoundedReevaluationTests
    {
        private const string TenantA = TransferScenario.TenantA;
        private const string Account = TransferScenario.OperatingAccount;
        private const decimal Pending = 12_500m;

        [Fact]
        public async Task Approved_WhenFundsWereSpentMeanwhile_IsInvalidatedWithoutDebit()
        {
            var (scenario, sessionId) = await ApprovedSessionAsync();
            await DebitAsync(scenario, 45_000m); // another transfer executes while the session waits

            await AssertInvalidatedAsync(scenario, sessionId, TransferRejectionCode.InsufficientFunds);
            Assert.Equal(TransferScenario.OpeningBalance - 45_000m, scenario.Ledger.GetBalance(TenantA, Account));
        }

        [Fact]
        public async Task Approved_WhenAccountWasFrozenMeanwhile_IsInvalidated()
        {
            var (scenario, sessionId) = await ApprovedSessionAsync();
            scenario.Ledger.Freeze(TenantA, Account);

            await AssertInvalidatedAsync(scenario, sessionId, TransferRejectionCode.AccountFrozen);
        }

        [Fact]
        public async Task Approved_WhenBeneficiaryWasBlockedMeanwhile_IsInvalidated()
        {
            var (scenario, sessionId) = await ApprovedSessionAsync();
            scenario.Ledger.BlockBeneficiary(TenantA, FakeChatClient.DestinationIban);

            await AssertInvalidatedAsync(scenario, sessionId, TransferRejectionCode.BeneficiaryBlocked);
        }

        [Fact]
        public async Task Approved_WhenDailyLimitWasConsumedMeanwhile_IsInvalidated()
        {
            var (scenario, sessionId) = await ApprovedSessionAsync();
            scenario.Ledger.SeedAccount(TenantA, Account, "EUR", 500_000m); // plenty of funds: only the limit binds
            await DebitAsync(scenario, 9_000m);
            await DebitAsync(scenario, 9_000m);
            await DebitAsync(scenario, 9_000m);
            await DebitAsync(scenario, 9_000m);
            await DebitAsync(scenario, 9_000m);
            await DebitAsync(scenario, 3_000m); // 48,000 of 60,000 used: 12,500 more would exceed it

            await AssertInvalidatedAsync(scenario, sessionId, TransferRejectionCode.DailyLimitExceeded);
        }

        [Fact]
        public async Task Approved_WhenTenantPolicyWasTightenedMeanwhile_IsInvalidated()
        {
            var (scenario, sessionId) = await ApprovedSessionAsync();
            scenario.PolicyA.MaxPerTransfer = 10_000m;

            await AssertInvalidatedAsync(scenario, sessionId, TransferRejectionCode.ExceedsPerTransferLimit);
        }

        [Fact]
        public async Task Approved_WhenDomainIsUnchanged_ExecutesWithoutReapplyingTheThreshold()
        {
            var (scenario, sessionId) = await ApprovedSessionAsync();

            Assert.True(await scenario.CreateProcessor().ProcessNextAsync());

            var session = await scenario.Repository.GetAsync(TenantA, sessionId);
            Assert.Equal(HitlSessionStatus.Completed, session?.Status);
            Assert.Null(session?.InvalidationReason);
            Assert.Equal(TransferScenario.OpeningBalance - Pending, scenario.Ledger.GetBalance(TenantA, Account));
        }

        [Fact]
        public async Task Approved_WhenAlreadyExecutedBeforeACrash_CompletesEvenIfPolicyChanged()
        {
            // The effect happened under the old policy; a reclaimed lease must not re-judge it.
            var (scenario, sessionId) = await ApprovedSessionAsync();
            await scenario.Ledger.ExecuteAsync(new TransferExecutionRequest(
                sessionId,
                TransferScenario.Command(Pending),
                scenario.PolicyA.DailyLimit,
                scenario.Time.GetUtcNow()));
            scenario.PolicyA.MaxPerTransfer = 1_000m;

            Assert.True(await scenario.CreateProcessor().ProcessNextAsync());

            Assert.Equal(HitlSessionStatus.Completed, (await scenario.Repository.GetAsync(TenantA, sessionId))?.Status);
            Assert.Equal(1, scenario.Ledger.ExecutedCount);
        }

        [Fact]
        public async Task Approved_WhenStateChangesBetweenCheckAndDebit_AtomicGuardStillInvalidates()
        {
            // Snapshot says "fine", but the executor's atomic guard sees the latest state (TOCTOU).
            var (scenario, sessionId) = await ApprovedSessionAsync();
            var racingExecutor = new RacingExecutor(TransferExecutionStatus.InsufficientFunds);

            Assert.True(await scenario.CreateProcessor(racingExecutor).ProcessNextAsync());

            var session = await scenario.Repository.GetAsync(TenantA, sessionId);
            Assert.Equal(HitlSessionStatus.Invalidated, session?.Status);
            Assert.StartsWith(nameof(TransferRejectionCode.InsufficientFunds), session?.InvalidationReason);
            Assert.Equal(1, racingExecutor.CallCount);
        }

        private static async Task<(TransferScenario Scenario, Guid SessionId)> ApprovedSessionAsync()
        {
            var scenario = new TransferScenario();
            var sessionId = Guid.NewGuid();
            await scenario.Repository.CreatePendingAsync(scenario.PendingSession(sessionId, Pending));
            await scenario.CreateAgentService().ResumeAsync(sessionId, true);
            return (scenario, sessionId);
        }

        private static Task DebitAsync(TransferScenario scenario, decimal amount) =>
            scenario.Ledger.ExecuteAsync(new TransferExecutionRequest(
                Guid.NewGuid(),
                TransferScenario.Command(amount),
                scenario.PolicyA.DailyLimit,
                scenario.Time.GetUtcNow()));

        private static async Task AssertInvalidatedAsync(TransferScenario scenario, Guid sessionId, TransferRejectionCode expected)
        {
            var executedBefore = scenario.Ledger.ExecutedCount;

            Assert.True(await scenario.CreateProcessor().ProcessNextAsync());

            var session = await scenario.Repository.GetAsync(TenantA, sessionId);
            Assert.Equal(HitlSessionStatus.Invalidated, session?.Status);
            Assert.StartsWith(expected.ToString(), session?.InvalidationReason);
            Assert.Equal(executedBefore, scenario.Ledger.ExecutedCount);
            Assert.Equal(0, scenario.Chat.CallCount);

            // Terminal: the worker never picks it up again.
            Assert.False(await scenario.CreateProcessor().ProcessNextAsync());
        }

        private sealed class RacingExecutor(TransferExecutionStatus status) : ITransferExecutor
        {
            public int CallCount { get; private set; }

            public Task<TransferExecutionStatus> ExecuteAsync(TransferExecutionRequest request, CancellationToken cancellationToken = default)
            {
                CallCount++;
                return Task.FromResult(status);
            }
        }
    }
}
