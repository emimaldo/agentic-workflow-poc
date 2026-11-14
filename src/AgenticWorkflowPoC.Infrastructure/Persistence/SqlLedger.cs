using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Plugins.Financial;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace AgenticWorkflowPoC.Infrastructure.Persistence
{
    public sealed class SqlLedgerReader : ILedgerReader
    {
        private readonly string _connectionString;

        public SqlLedgerReader(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("AgenticDatabase")
                ?? throw new InvalidOperationException("Database connection string is missing.");
        }

        public async Task<LedgerSnapshot> GetSnapshotAsync(LedgerQuery query, CancellationToken cancellationToken = default)
        {
            // Advisory, read-committed snapshot. Final guards run atomically in SqlTransferExecutor.
            // Isolation is declared explicitly: pooled connections keep the level of their previous user.
            const string sql = """
                SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

                SELECT Currency, Balance, IsFrozen
                FROM dbo.Accounts
                WHERE TenantId = @TenantId AND AccountId = @SourceAccountId;

                SELECT COALESCE(SUM(Amount), 0)
                FROM dbo.Transfers
                WHERE TenantId = @TenantId AND SourceAccountId = @SourceAccountId
                  AND ExecutedAt >= @DayStart AND ExecutedAt < @DayEnd;

                SELECT CAST(CASE WHEN EXISTS (
                    SELECT 1 FROM dbo.BlockedBeneficiaries
                    WHERE TenantId = @TenantId AND Iban = @DestinationAccount) THEN 1 ELSE 0 END AS bit);

                SELECT CAST(CASE WHEN EXISTS (
                    SELECT 1 FROM dbo.Transfers
                    WHERE TenantId = @TenantId AND OperationId = @OperationId) THEN 1 ELSE 0 END AS bit);
                """;

            var dayStart = LedgerDay.Start(query.AsOfUtc);
            await using var connection = new SqlConnection(_connectionString);
            using var results = await connection.QueryMultipleAsync(new CommandDefinition(
                sql,
                new
                {
                    query.TenantId,
                    query.SourceAccountId,
                    query.DestinationAccount,
                    query.OperationId,
                    DayStart = dayStart,
                    DayEnd = dayStart.AddDays(1)
                },
                cancellationToken: cancellationToken));

            var account = await results.ReadSingleOrDefaultAsync<AccountRow>();
            var debitedToday = await results.ReadSingleAsync<decimal>();
            var beneficiaryBlocked = await results.ReadSingleAsync<bool>();
            var alreadyExecuted = await results.ReadSingleAsync<bool>();

            return new LedgerSnapshot(
                account is not null,
                account?.Currency,
                account?.Balance ?? 0m,
                account?.IsFrozen ?? false,
                debitedToday,
                beneficiaryBlocked,
                alreadyExecuted);
        }
    }

    public sealed class SqlTransferExecutor : ITransferExecutor
    {
        private readonly string _connectionString;

        public SqlTransferExecutor(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("AgenticDatabase")
                ?? throw new InvalidOperationException("Database connection string is missing.");
        }

        public async Task<TransferExecutionStatus> ExecuteAsync(
            TransferExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            try
            {
                return await ExecuteSerializableAsync(connection, request, cancellationToken);
            }
            finally
            {
                await ResetIsolationLevelAsync(connection);
            }
        }

        /// <summary>
        /// SQL Server keeps a session's isolation level when the connection goes back to the pool,
        /// so a pooled connection would otherwise leak SERIALIZABLE into unrelated queries
        /// (e.g. the READPAST work-queue claim, which then fails).
        /// </summary>
        private static async Task ResetIsolationLevelAsync(SqlConnection connection)
        {
            if (connection.State != ConnectionState.Open)
            {
                return;
            }

            try
            {
                await connection.ExecuteAsync("SET TRANSACTION ISOLATION LEVEL READ COMMITTED;");
            }
            catch (SqlException)
            {
                // The connection is unusable; make sure it is not handed out again with a stale level.
                SqlConnection.ClearPool(connection);
            }
        }

        private static async Task<TransferExecutionStatus> ExecuteSerializableAsync(
            SqlConnection connection,
            TransferExecutionRequest request,
            CancellationToken cancellationToken)
        {
            var command = request.Command;
            var dayStart = LedgerDay.Start(request.AsOfUtc);

            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            // 1. Lock the source account first: every operation on the same account serializes here,
            //    which closes the check-then-act (TOCTOU) window for balance and daily limit.
            const string lockAccountSql = """
                SELECT Currency, Balance, IsFrozen
                FROM dbo.Accounts WITH (UPDLOCK, HOLDLOCK)
                WHERE TenantId = @TenantId AND AccountId = @SourceAccountId;
                """;
            var account = await connection.QuerySingleOrDefaultAsync<AccountRow>(new CommandDefinition(
                lockAccountSql,
                new { command.TenantId, command.SourceAccountId },
                transaction,
                cancellationToken: cancellationToken));

            // 2. Idempotency: the operation id is the HITL session id.
            const string findTransferSql = """
                SELECT SourceAccountId, DestinationAccount, Amount, Currency
                FROM dbo.Transfers WITH (UPDLOCK, HOLDLOCK)
                WHERE TenantId = @TenantId AND OperationId = @OperationId;
                """;
            var existing = await connection.QuerySingleOrDefaultAsync<TransferRow>(new CommandDefinition(
                findTransferSql,
                new { command.TenantId, request.OperationId },
                transaction,
                cancellationToken: cancellationToken));

            if (existing is not null)
            {
                if (!existing.Matches(command))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return TransferExecutionStatus.IdempotencyConflict;
                }

                await transaction.CommitAsync(cancellationToken);
                return TransferExecutionStatus.AlreadyExecuted;
            }

            // 3. Atomic guards, evaluated under the account lock.
            var guard = account switch
            {
                null => TransferExecutionStatus.AccountNotFound,
                { IsFrozen: true } => TransferExecutionStatus.AccountFrozen,
                _ when !string.Equals(account.Currency, command.Currency, StringComparison.Ordinal) => TransferExecutionStatus.CurrencyMismatch,
                _ when account.Balance < command.Amount => TransferExecutionStatus.InsufficientFunds,
                _ => TransferExecutionStatus.Executed
            };

            if (guard == TransferExecutionStatus.Executed)
            {
                const string dailyTotalSql = """
                    SELECT COALESCE(SUM(Amount), 0)
                    FROM dbo.Transfers WITH (HOLDLOCK)
                    WHERE TenantId = @TenantId AND SourceAccountId = @SourceAccountId
                      AND ExecutedAt >= @DayStart AND ExecutedAt < @DayEnd;
                    """;
                var debitedToday = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
                    dailyTotalSql,
                    new { command.TenantId, command.SourceAccountId, DayStart = dayStart, DayEnd = dayStart.AddDays(1) },
                    transaction,
                    cancellationToken: cancellationToken));

                if (debitedToday + command.Amount > request.DailyLimit)
                {
                    guard = TransferExecutionStatus.DailyLimitExceeded;
                }
            }

            if (guard != TransferExecutionStatus.Executed)
            {
                await transaction.RollbackAsync(cancellationToken);
                return guard;
            }

            // 4. Debit + record in the same SERIALIZABLE transaction.
            const string applySql = """
                UPDATE dbo.Accounts
                SET Balance = Balance - @Amount
                WHERE TenantId = @TenantId AND AccountId = @SourceAccountId;

                INSERT INTO dbo.Transfers
                    (TenantId, OperationId, SourceAccountId, DestinationAccount, Amount, Currency, ExecutedAt)
                VALUES
                    (@TenantId, @OperationId, @SourceAccountId, @DestinationAccount, @Amount, @Currency, @ExecutedAt);
                """;
            await connection.ExecuteAsync(new CommandDefinition(
                applySql,
                new
                {
                    command.TenantId,
                    request.OperationId,
                    command.SourceAccountId,
                    command.DestinationAccount,
                    command.Amount,
                    command.Currency,
                    ExecutedAt = request.AsOfUtc
                },
                transaction,
                cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            return TransferExecutionStatus.Executed;
        }

        private sealed class TransferRow
        {
            public string SourceAccountId { get; init; } = string.Empty;
            public string DestinationAccount { get; init; } = string.Empty;
            public decimal Amount { get; init; }
            public string Currency { get; init; } = string.Empty;

            public bool Matches(Core.Entities.EnrichedTransferCommand command) =>
                string.Equals(SourceAccountId, command.SourceAccountId, StringComparison.Ordinal)
                && string.Equals(DestinationAccount, command.DestinationAccount, StringComparison.Ordinal)
                && Amount == command.Amount
                && string.Equals(Currency, command.Currency, StringComparison.Ordinal);
        }
    }

    internal sealed class AccountRow
    {
        public string Currency { get; init; } = string.Empty;
        public decimal Balance { get; init; }
        public bool IsFrozen { get; init; }
    }

    internal static class LedgerDay
    {
        /// <summary>Daily limits are evaluated per UTC calendar day.</summary>
        public static DateTimeOffset Start(DateTimeOffset asOf)
        {
            var utc = asOf.ToUniversalTime();
            return new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);
        }
    }
}
