using System;
using System.Linq;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Core.Entities;
using AgenticWorkflowPoC.Infrastructure.Persistence;
using AgenticWorkflowPoC.Plugins.Financial;
using AgenticWorkflowPoC.Tests.Support;
using Dapper;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AgenticWorkflowPoC.Tests.Integration
{
    /// <summary>
    /// Zero-token tests against a real SQL Server (Testcontainers): atomic debits, idempotency,
    /// concurrency guards and tenant isolation of the T-SQL actually shipped in this repository.
    /// </summary>
    [Collection(SqlServerCollection.Name)]
    public class SqlPersistenceTests
    {
        private const string Account = "OPS-EUR-001";
        private const string Iban = "DE89370400440532013000";
        private static readonly DateTimeOffset Now = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

        private readonly SqlServerFixture _sql;

        public SqlPersistenceTests(SqlServerFixture sql)
        {
            _sql = sql;
        }

        // ---- Ledger ---------------------------------------------------------------------

        [DockerFact]
        public async Task Executor_DebitsAndRecordsAtomically_AndReplayIsIdempotent()
        {
            var tenant = NewTenant();
            await _sql.SeedAccountAsync(tenant, Account, "EUR", 1_000m);
            var executor = new SqlTransferExecutor(_sql.Configuration);
            var request = Request(tenant, Guid.NewGuid(), 250.55m);

            var first = await executor.ExecuteAsync(request);
            var replay = await executor.ExecuteAsync(request);

            Assert.Equal(TransferExecutionStatus.Executed, first);
            Assert.Equal(TransferExecutionStatus.AlreadyExecuted, replay);
            Assert.Equal(749.45m, await _sql.GetBalanceAsync(tenant, Account));
            Assert.Equal(1, await CountTransfersAsync(tenant));
        }

        [DockerFact]
        public async Task Executor_ReusedOperationIdForDifferentAmount_ThrowsAndChangesNothing()
        {
            var tenant = NewTenant();
            await _sql.SeedAccountAsync(tenant, Account, "EUR", 1_000m);
            var executor = new SqlTransferExecutor(_sql.Configuration);
            var operationId = Guid.NewGuid();
            await executor.ExecuteAsync(Request(tenant, operationId, 100m));

            await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(Request(tenant, operationId, 200m)));

            Assert.Equal(900m, await _sql.GetBalanceAsync(tenant, Account));
        }

        [DockerFact]
        public async Task Executor_ConcurrentDebits_NeverOverdrawTheAccount()
        {
            var tenant = NewTenant();
            await _sql.SeedAccountAsync(tenant, Account, "EUR", 1_000m);
            var executor = new SqlTransferExecutor(_sql.Configuration);

            var results = await Task.WhenAll(Enumerable.Range(0, 20)
                .Select(_ => executor.ExecuteAsync(Request(tenant, Guid.NewGuid(), 100m))));

            Assert.Equal(10, results.Count(r => r == TransferExecutionStatus.Executed));
            Assert.Equal(10, results.Count(r => r == TransferExecutionStatus.InsufficientFunds));
            Assert.Equal(0m, await _sql.GetBalanceAsync(tenant, Account));
        }

        [DockerFact]
        public async Task Executor_ConcurrentDebits_NeverExceedTheDailyLimit()
        {
            var tenant = NewTenant();
            await _sql.SeedAccountAsync(tenant, Account, "EUR", 100_000m);
            var executor = new SqlTransferExecutor(_sql.Configuration);

            var results = await Task.WhenAll(Enumerable.Range(0, 10)
                .Select(_ => executor.ExecuteAsync(Request(tenant, Guid.NewGuid(), 400m, dailyLimit: 1_000m))));

            Assert.Equal(2, results.Count(r => r == TransferExecutionStatus.Executed));
            Assert.Equal(8, results.Count(r => r == TransferExecutionStatus.DailyLimitExceeded));
            Assert.Equal(99_200m, await _sql.GetBalanceAsync(tenant, Account));
        }

        [DockerFact]
        public async Task Executor_FrozenOrMissingAccount_IsRejectedWithoutSideEffects()
        {
            var tenant = NewTenant();
            await _sql.SeedAccountAsync(tenant, Account, "EUR", 1_000m, frozen: true);
            var executor = new SqlTransferExecutor(_sql.Configuration);

            Assert.Equal(TransferExecutionStatus.AccountFrozen, await executor.ExecuteAsync(Request(tenant, Guid.NewGuid(), 10m)));
            Assert.Equal(TransferExecutionStatus.AccountNotFound, await executor.ExecuteAsync(Request(NewTenant(), Guid.NewGuid(), 10m)));
            Assert.Equal(0, await CountTransfersAsync(tenant));
        }

        [DockerFact]
        public async Task LedgerReader_ReturnsTenantScopedSnapshot()
        {
            var tenant = NewTenant();
            var otherTenant = NewTenant();
            await _sql.SeedAccountAsync(tenant, Account, "EUR", 1_000m);
            await _sql.SeedAccountAsync(otherTenant, Account, "EUR", 5_000m);
            var executor = new SqlTransferExecutor(_sql.Configuration);
            var executed = Guid.NewGuid();
            await executor.ExecuteAsync(Request(tenant, executed, 300m));
            await executor.ExecuteAsync(Request(otherTenant, Guid.NewGuid(), 700m));
            await using (var connection = await _sql.OpenAsync())
            {
                await connection.ExecuteAsync(
                    "INSERT INTO dbo.BlockedBeneficiaries (TenantId, Iban, BlockedAt) VALUES (@tenant, @Iban, SYSDATETIMEOFFSET());",
                    new { tenant, Iban });
            }

            var reader = new SqlLedgerReader(_sql.Configuration);
            var snapshot = await reader.GetSnapshotAsync(new LedgerQuery(tenant, Account, Iban, executed, Now));
            var otherSnapshot = await reader.GetSnapshotAsync(new LedgerQuery(otherTenant, Account, Iban, executed, Now));

            Assert.Equal(new LedgerSnapshot(true, "EUR", 700m, false, 300m, true, true), snapshot);
            Assert.Equal(new LedgerSnapshot(true, "EUR", 4_300m, false, 700m, false, false), otherSnapshot);
        }

        // ---- HITL sessions -----------------------------------------------------------------

        [DockerFact]
        public async Task HitlRepository_IsTenantScoped_AndResolvesOnlyOnce()
        {
            var tenant = NewTenant();
            var repository = new SqlHitlSessionRepository(_sql.Configuration);
            var sessionId = Guid.NewGuid();
            await repository.TryCreatePendingAsync(Session(sessionId, tenant));

            Assert.Null(await repository.GetAsync(NewTenant(), sessionId));
            Assert.False(await repository.TryResolveAsync(NewTenant(), sessionId, HitlSessionStatus.Approved, "intruder", Now));

            var resolutions = await Task.WhenAll(
                repository.TryResolveAsync(tenant, sessionId, HitlSessionStatus.Approved, "op-1", Now),
                repository.TryResolveAsync(tenant, sessionId, HitlSessionStatus.Rejected, "op-2", Now));

            Assert.Single(resolutions, won => won);
            var stored = await repository.GetAsync(tenant, sessionId);
            Assert.NotEqual(HitlSessionStatus.PendingReview, stored?.Status);
            Assert.Equal(Now, stored?.ResolvedAt);
            Assert.Contains(stored?.ResolvedBy, new[] { "op-1", "op-2" });
        }

        [DockerFact]
        public async Task HitlRepository_ConcurrentWorkersClaimEachSessionOnce_AndInvalidationIsRecorded()
        {
            var tenant = NewTenant();
            var repository = new SqlHitlSessionRepository(_sql.Configuration);
            var sessionId = Guid.NewGuid();
            await repository.TryCreatePendingAsync(Session(sessionId, tenant));
            await repository.TryResolveAsync(tenant, sessionId, HitlSessionStatus.Approved, "op", Now);

            // Other approved sessions from earlier tests may also be claimable; collect only ours.
            var claims = await Task.WhenAll(Enumerable.Range(0, 8)
                .Select(_ => repository.TryClaimNextApprovedAsync(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2))));
            Assert.Single(claims, c => c?.SessionId == sessionId);

            Assert.True(await repository.TryInvalidateAsync(tenant, sessionId, "AccountFrozen: frozen"));
            var stored = await repository.GetAsync(tenant, sessionId);
            Assert.Equal(HitlSessionStatus.Invalidated, stored?.Status);
            Assert.Equal("AccountFrozen: frozen", stored?.InvalidationReason);
            Assert.Equal(1, stored?.ExecutionAttempts);
        }

        [DockerFact]
        public async Task HitlRepository_SameSessionIdInTwoTenants_DoesNotCollide_ButRepeatsWithinATenantDo()
        {
            var tenantA = NewTenant();
            var tenantB = NewTenant();
            var repository = new SqlHitlSessionRepository(_sql.Configuration);
            var sharedId = Guid.NewGuid();

            Assert.True(await repository.TryCreatePendingAsync(Session(sharedId, tenantA)));
            Assert.True(await repository.TryCreatePendingAsync(Session(sharedId, tenantB)));
            Assert.False(await repository.TryCreatePendingAsync(Session(sharedId, tenantA)));
        }

        [DockerFact]
        public async Task Migrations_UpgradeALegacyDatabaseThrough001To005()
        {
            // An existing database created by the very first script, upgraded step by step.
            var connectionString = await _sql.CreateDatabaseFromScriptsAsync(
                "LegacyUpgrade",
                "scripts/001-create-hitl-sessions.sql",
                "scripts/002-hitl-tenant-and-work-queue.sql",
                "scripts/003-fintech-ledger.sql",
                "scripts/004-hitl-serialized-context.sql",
                "scripts/005-hitl-tenant-scoped-key.sql",
                "scripts/005-hitl-tenant-scoped-key.sql"); // idempotent re-run

            await using var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
            var keyColumns = await connection.QueryAsync<string>("""
                SELECT c.name
                FROM sys.key_constraints kc
                JOIN sys.index_columns ic ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
                JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                WHERE kc.parent_object_id = OBJECT_ID(N'dbo.HitlSessions') AND kc.type = 'PK'
                ORDER BY ic.key_ordinal;
                """);
            var staffTableExists = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.tables WHERE name = N'StaffAvailabilityOverrides';");

            Assert.Equal(["TenantId", "SessionId"], keyColumns);
            Assert.Equal(0, staffTableExists);

            var repository = new SqlHitlSessionRepository(new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?> { ["ConnectionStrings:AgenticDatabase"] = connectionString })
                .Build());
            var sessionId = Guid.NewGuid();
            Assert.True(await repository.TryCreatePendingAsync(Session(sessionId, "tenant-a")));
            Assert.True(await repository.TryResolveAsync("tenant-a", sessionId, HitlSessionStatus.Approved, "op", Now));
            Assert.Equal("op", (await repository.GetAsync("tenant-a", sessionId))?.ResolvedBy);
        }

        private static string NewTenant() => $"t-{Guid.NewGuid():N}";

        private static TransferExecutionRequest Request(string tenant, Guid operationId, decimal amount, decimal dailyLimit = 1_000_000m) =>
            new(
                operationId,
                new EnrichedTransferCommand
                {
                    TenantId = tenant,
                    SourceAccountId = Account,
                    DestinationAccount = Iban,
                    Amount = amount,
                    Currency = "EUR"
                },
                dailyLimit,
                Now);

        private static HitlSession Session(Guid sessionId, string tenant) =>
            new(sessionId, tenant, "Requires review.", "{}", HitlSessionStatus.PendingReview, Now);

        private async Task<int> CountTransfersAsync(string tenant)
        {
            await using var connection = await _sql.OpenAsync();
            return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Transfers WHERE TenantId = @tenant;", new { tenant });
        }
    }
}
