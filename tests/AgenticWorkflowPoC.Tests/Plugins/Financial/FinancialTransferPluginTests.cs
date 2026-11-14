using System;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Plugins.Financial;
using AgenticWorkflowPoC.Tests.Support;
using Xunit;

namespace AgenticWorkflowPoC.Tests.Plugins.Financial
{
    public class FinancialTransferPluginTests
    {
        private const string Tenant = TransferScenario.TenantA;
        private const string Account = TransferScenario.OperatingAccount;

        [Fact]
        public async Task ExecuteTransfer_BelowThreshold_CompletesAndDebitsOnce()
        {
            var scenario = new TransferScenario();

            var outcome = await scenario.CreatePlugin().ExecuteTransferAsync(Guid.NewGuid(), TransferScenario.Command(250m));

            Assert.Equal(TransferOutcomeKind.Completed, outcome.Kind);
            Assert.False(scenario.Hitl.IsSuspended);
            Assert.Equal(TransferScenario.OpeningBalance - 250m, scenario.Ledger.GetBalance(Tenant, Account));
            Assert.DoesNotContain(FakeIbanBody, outcome.Message);
        }

        [Fact]
        public async Task ExecuteTransfer_AboveThreshold_SuspendsWithoutDebiting()
        {
            var scenario = new TransferScenario();

            var outcome = await scenario.CreatePlugin().ExecuteTransferAsync(Guid.NewGuid(), TransferScenario.Command(12_500m));

            Assert.Equal(TransferOutcomeKind.RequiresApproval, outcome.Kind);
            Assert.True(scenario.Hitl.IsSuspended);
            Assert.Contains("approval threshold", scenario.Hitl.Reason);
            Assert.Equal(0, scenario.Ledger.ExecutedCount);
            Assert.Equal(TransferScenario.OpeningBalance, scenario.Ledger.GetBalance(Tenant, Account));
        }

        [Fact]
        public async Task ExecuteTransfer_AtThreshold_DoesNotRequireApproval()
        {
            var scenario = new TransferScenario();

            var outcome = await scenario.CreatePlugin().ExecuteTransferAsync(Guid.NewGuid(), TransferScenario.Command(10_000m));

            Assert.Equal(TransferOutcomeKind.Completed, outcome.Kind);
        }

        [Fact]
        public async Task ExecuteTransfer_InsufficientFunds_RejectsBeforeAskingForApproval()
        {
            var scenario = new TransferScenario();
            scenario.Ledger.SeedAccount(Tenant, Account, "EUR", 100m);

            var outcome = await scenario.CreatePlugin().ExecuteTransferAsync(Guid.NewGuid(), TransferScenario.Command(12_500m));

            AssertRejected(outcome, TransferRejectionCode.InsufficientFunds);
            Assert.False(scenario.Hitl.IsSuspended);
        }

        [Fact]
        public async Task ExecuteTransfer_FrozenAccount_Rejects()
        {
            var scenario = new TransferScenario();
            scenario.Ledger.Freeze(Tenant, Account);

            var outcome = await scenario.CreatePlugin().ExecuteTransferAsync(Guid.NewGuid(), TransferScenario.Command(250m));

            AssertRejected(outcome, TransferRejectionCode.AccountFrozen);
        }

        [Fact]
        public async Task ExecuteTransfer_BlockedBeneficiary_Rejects()
        {
            var scenario = new TransferScenario();
            scenario.Ledger.BlockBeneficiary(Tenant, Api.Development.FakeChatClient.DestinationIban);

            var outcome = await scenario.CreatePlugin().ExecuteTransferAsync(Guid.NewGuid(), TransferScenario.Command(250m));

            AssertRejected(outcome, TransferRejectionCode.BeneficiaryBlocked);
        }

        [Fact]
        public async Task ExecuteTransfer_ExceedingDailyLimit_Rejects()
        {
            var scenario = new TransferScenario();
            scenario.PolicyA.DailyLimit = 1_000m;
            var plugin = scenario.CreatePlugin();

            var first = await plugin.ExecuteTransferAsync(Guid.NewGuid(), TransferScenario.Command(600m));
            var second = await plugin.ExecuteTransferAsync(Guid.NewGuid(), TransferScenario.Command(600m));

            Assert.Equal(TransferOutcomeKind.Completed, first.Kind);
            AssertRejected(second, TransferRejectionCode.DailyLimitExceeded);
            Assert.Equal(1, scenario.Ledger.ExecutedCount);
        }

        [Fact]
        public async Task ExecuteTransfer_CurrencyNotEnabledForTenant_Rejects()
        {
            var scenario = new TransferScenario();

            var outcome = await scenario.CreatePlugin().ExecuteTransferAsync(Guid.NewGuid(), TransferScenario.Command(250m, currency: "GBP"));

            AssertRejected(outcome, TransferRejectionCode.CurrencyNotAllowed);
        }

        [Fact]
        public async Task ExecuteTransfer_CurrencyDifferentFromAccount_Rejects()
        {
            var scenario = new TransferScenario();

            var outcome = await scenario.CreatePlugin().ExecuteTransferAsync(Guid.NewGuid(), TransferScenario.Command(250m, currency: "USD"));

            AssertRejected(outcome, TransferRejectionCode.CurrencyMismatch);
        }

        [Fact]
        public async Task ExecuteTransfer_AbovePerTransferLimit_Rejects()
        {
            var scenario = new TransferScenario();
            scenario.PolicyA.MaxPerTransfer = 200m;

            var outcome = await scenario.CreatePlugin().ExecuteTransferAsync(Guid.NewGuid(), TransferScenario.Command(250m));

            AssertRejected(outcome, TransferRejectionCode.ExceedsPerTransferLimit);
        }

        [Fact]
        public async Task ExecuteTransfer_SameOperationReplayed_DebitsOnlyOnce()
        {
            var scenario = new TransferScenario();
            var plugin = scenario.CreatePlugin();
            var operationId = Guid.NewGuid();

            var first = await plugin.ExecuteTransferAsync(operationId, TransferScenario.Command(250m));
            var replay = await plugin.ExecuteTransferAsync(operationId, TransferScenario.Command(250m));

            Assert.Equal(TransferOutcomeKind.Completed, first.Kind);
            Assert.Equal(TransferOutcomeKind.Completed, replay.Kind);
            Assert.Equal(1, scenario.Ledger.ExecutedCount);
            Assert.Equal(TransferScenario.OpeningBalance - 250m, scenario.Ledger.GetBalance(Tenant, Account));
        }

        [Fact]
        public async Task ExecuteTransfer_OperationIdReusedForDifferentAmount_Throws()
        {
            var scenario = new TransferScenario();
            var plugin = scenario.CreatePlugin();
            var operationId = Guid.NewGuid();
            await plugin.ExecuteTransferAsync(operationId, TransferScenario.Command(250m));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => plugin.ExecuteTransferAsync(operationId, TransferScenario.Command(300m)));
        }

        [Fact]
        public async Task ExecuteApprovedTransfer_AboveThreshold_ExecutesWithoutReopeningReview()
        {
            var scenario = new TransferScenario();

            var outcome = await scenario.CreatePlugin().ExecuteApprovedTransferAsync(Guid.NewGuid(), TransferScenario.Command(12_500m));

            Assert.Equal(TransferOutcomeKind.Completed, outcome.Kind);
            Assert.False(scenario.Hitl.IsSuspended);
            Assert.Equal(TransferScenario.OpeningBalance - 12_500m, scenario.Ledger.GetBalance(Tenant, Account));
        }

        private const string FakeIbanBody = "370400440532";

        private static void AssertRejected(TransferOutcome outcome, TransferRejectionCode expected)
        {
            Assert.Equal(TransferOutcomeKind.Rejected, outcome.Kind);
            Assert.Equal(expected, outcome.RejectionCode);
        }
    }
}
