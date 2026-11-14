using System;
using System.Threading;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Core.Entities;
using AgenticWorkflowPoC.Core.Interfaces;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace AgenticWorkflowPoC.Infrastructure.Persistence
{
    public sealed class SqlHitlSessionRepository : IHitlSessionRepository
    {
        private readonly string _connectionString;

        public SqlHitlSessionRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("AgenticDatabase")
                ?? throw new InvalidOperationException("Database connection string is missing.");
        }

        public async Task<HitlSession?> GetAsync(string tenantId, Guid sessionId, CancellationToken cancellationToken = default)
        {
            const string sql = """
                SELECT SessionId, TenantId, Reason, PendingActionPayload, Status, SuspendedAt,
                       ExecutionAttempts, ProcessingStartedAt
                FROM dbo.HitlSessions
                WHERE TenantId = @TenantId AND SessionId = @SessionId;
                """;

            await using var connection = new SqlConnection(_connectionString);
            var row = await connection.QuerySingleOrDefaultAsync<HitlSessionRow>(
                new CommandDefinition(sql, new { TenantId = tenantId, SessionId = sessionId }, cancellationToken: cancellationToken));

            return row is null
                ? null
                : new HitlSession(
                    row.SessionId,
                    row.TenantId,
                    row.Reason,
                    row.PendingActionPayload,
                    Enum.Parse<HitlSessionStatus>(row.Status),
                    row.SuspendedAt,
                    row.ExecutionAttempts,
                    row.ProcessingStartedAt);
        }

        public async Task CreatePendingAsync(HitlSession session, CancellationToken cancellationToken = default)
        {
            const string sql = """
                INSERT INTO dbo.HitlSessions
                    (SessionId, TenantId, Reason, PendingActionPayload, Status, SuspendedAt, ExecutionAttempts)
                VALUES
                    (@SessionId, @TenantId, @Reason, @PendingActionPayload, @Status, @SuspendedAt, 0);
                """;

            await using var connection = new SqlConnection(_connectionString);
            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new
                {
                    session.SessionId,
                    session.TenantId,
                    session.Reason,
                    session.PendingActionPayload,
                    Status = session.Status.ToString(),
                    session.SuspendedAt
                },
                cancellationToken: cancellationToken));
        }

        public async Task<bool> TryTransitionAsync(
            string tenantId,
            Guid sessionId,
            HitlSessionStatus expectedStatus,
            HitlSessionStatus newStatus,
            CancellationToken cancellationToken = default)
        {
            const string sql = """
                UPDATE dbo.HitlSessions
                SET Status = @NewStatus,
                    ProcessingStartedAt = CASE
                        WHEN @NewStatus = 'Processing' THEN TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')
                        ELSE NULL
                    END
                WHERE TenantId = @TenantId AND SessionId = @SessionId AND Status = @ExpectedStatus;
                """;

            await using var connection = new SqlConnection(_connectionString);
            var affectedRows = await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new
                {
                    TenantId = tenantId,
                    SessionId = sessionId,
                    ExpectedStatus = expectedStatus.ToString(),
                    NewStatus = newStatus.ToString()
                },
                cancellationToken: cancellationToken));

            return affectedRows == 1;
        }

        public async Task<HitlSession?> TryClaimNextApprovedAsync(
            DateTimeOffset now,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken = default)
        {
            const string sql = """
                ;WITH candidate AS
                (
                    SELECT TOP (1) *
                    FROM dbo.HitlSessions WITH (UPDLOCK, READPAST, ROWLOCK)
                    WHERE Status = @Approved
                       OR (Status = @Processing AND ProcessingStartedAt <= @LeaseCutoff)
                    ORDER BY SuspendedAt, SessionId
                )
                UPDATE candidate
                SET Status = @Processing,
                    ProcessingStartedAt = @Now,
                    ExecutionAttempts = ExecutionAttempts + 1
                OUTPUT INSERTED.SessionId, INSERTED.TenantId, INSERTED.Reason,
                       INSERTED.PendingActionPayload, INSERTED.Status, INSERTED.SuspendedAt,
                       INSERTED.ExecutionAttempts, INSERTED.ProcessingStartedAt;
                """;

            await using var connection = new SqlConnection(_connectionString);
            var row = await connection.QuerySingleOrDefaultAsync<HitlSessionRow>(new CommandDefinition(
                sql,
                new
                {
                    Approved = HitlSessionStatus.Approved.ToString(),
                    Processing = HitlSessionStatus.Processing.ToString(),
                    Now = now,
                    LeaseCutoff = now - leaseDuration
                },
                cancellationToken: cancellationToken));

            return row is null
                ? null
                : new HitlSession(
                    row.SessionId,
                    row.TenantId,
                    row.Reason,
                    row.PendingActionPayload,
                    Enum.Parse<HitlSessionStatus>(row.Status),
                    row.SuspendedAt,
                    row.ExecutionAttempts,
                    row.ProcessingStartedAt);
        }

        private sealed class HitlSessionRow
        {
            public Guid SessionId { get; init; }
            public string TenantId { get; init; } = string.Empty;
            public string Reason { get; init; } = string.Empty;
            public string PendingActionPayload { get; init; } = string.Empty;
            public string Status { get; init; } = string.Empty;
            public DateTimeOffset SuspendedAt { get; init; }
            public int ExecutionAttempts { get; init; }
            public DateTimeOffset? ProcessingStartedAt { get; init; }
        }
    }
}
