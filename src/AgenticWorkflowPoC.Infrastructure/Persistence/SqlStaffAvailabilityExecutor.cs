using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Plugins.Operations;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace AgenticWorkflowPoC.Infrastructure.Persistence
{
    public sealed class SqlStaffAvailabilityExecutor : IStaffAvailabilityExecutor
    {
        private readonly string _connectionString;

        public SqlStaffAvailabilityExecutor(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("AgenticDatabase")
                ?? throw new InvalidOperationException("Database connection string is missing.");
        }

        public async Task ExecuteAsync(
            Guid operationId,
            string tenantId,
            string staffId,
            DateTimeOffset newAvailability,
            CancellationToken cancellationToken = default)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

            const string findSql = """
                SELECT StaffId, NewAvailability
                FROM dbo.StaffAvailabilityOverrides WITH (UPDLOCK, HOLDLOCK)
                WHERE SessionId = @SessionId AND TenantId = @TenantId;
                """;

            var existing = await connection.QuerySingleOrDefaultAsync<AvailabilityOverrideRow>(
                new CommandDefinition(findSql, new { SessionId = operationId, TenantId = tenantId }, transaction, cancellationToken: cancellationToken));

            if (existing is null)
            {
                const string insertSql = """
                    INSERT INTO dbo.StaffAvailabilityOverrides
                        (SessionId, TenantId, StaffId, NewAvailability, CreatedAt)
                    VALUES
                        (@SessionId, @TenantId, @StaffId, @NewAvailability, TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'));
                    """;

                await connection.ExecuteAsync(new CommandDefinition(
                    insertSql,
                    new { SessionId = operationId, TenantId = tenantId, StaffId = staffId, NewAvailability = newAvailability },
                    transaction,
                    cancellationToken: cancellationToken));
            }
            else if (!string.Equals(existing.StaffId, staffId, StringComparison.Ordinal)
                     || existing.NewAvailability != newAvailability)
            {
                throw new InvalidOperationException("An idempotency key was reused for a different availability operation.");
            }

            await transaction.CommitAsync(cancellationToken);
        }

        private sealed class AvailabilityOverrideRow
        {
            public string StaffId { get; init; } = string.Empty;
            public DateTimeOffset NewAvailability { get; init; }
        }
    }
}
