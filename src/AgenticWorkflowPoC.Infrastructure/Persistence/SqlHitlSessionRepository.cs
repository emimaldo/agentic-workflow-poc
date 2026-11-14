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
        private const string SelectColumns = """
            SessionId, TenantId, Reason, SerializedContext, Status, SuspendedAt,
            ExecutionAttempts, ProcessingStartedAt, ResolvedBy, ResolvedAt, InvalidationReason
            """;

        private readonly string _connectionString;

        public SqlHitlSessionRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("AgenticDatabase")
                ?? throw new InvalidOperationException("Database connection string is missing.");
        }

        public async Task<HitlSession?> GetAsync(string tenantId, Guid sessionId, CancellationToken cancellationToken = default)
        {
            const string sql = $"""
                SELECT {SelectColumns}
                FROM dbo.HitlSessions
                WHERE TenantId = @TenantId AND SessionId = @SessionId;
                """;

            await using var connection = new SqlConnection(_connectionString);
            var row = await connection.QuerySingleOrDefaultAsync<HitlSessionRow>(
                new CommandDefinition(sql, new { TenantId = tenantId, SessionId = sessionId }, cancellationToken: cancellationToken));

            return row?.ToEntity();
        }

        public async Task CreatePendingAsync(HitlSession session, CancellationToken cancellationToken = default)
        {
            const string sql = """
                INSERT INTO dbo.HitlSessions
                    (SessionId, TenantId, Reason, SerializedContext, Status, SuspendedAt, ExecutionAttempts)
                VALUES
                    (@SessionId, @TenantId, @Reason, @SerializedContext, @Status, @SuspendedAt, 0);
                """;

            await using var connection = new SqlConnection(_connectionString);
            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new
                {
                    session.SessionId,
                    session.TenantId,
                    session.Reason,
                    session.SerializedContext,
                    Status = session.Status.ToString(),
                    session.SuspendedAt
                },
                cancellationToken: cancellationToken));
        }

        public async Task<bool> TryResolveAsync(
            string tenantId,
            Guid sessionId,
            HitlSessionStatus resolution,
            string resolvedBy,
            DateTimeOffset resolvedAt,
            CancellationToken cancellationToken = default)
        {
            if (resolution is not (HitlSessionStatus.Approved or HitlSessionStatus.Rejected))
            {
                throw new ArgumentOutOfRangeException(nameof(resolution), "A HITL session can only be approved or rejected.");
            }

            // Compare-and-set: only one resume signal can ever win.
            const string sql = """
                UPDATE dbo.HitlSessions
                SET Status = @Resolution,
                    ResolvedBy = @ResolvedBy,
                    ResolvedAt = @ResolvedAt
                WHERE TenantId = @TenantId AND SessionId = @SessionId AND Status = @PendingReview;
                """;

            await using var connection = new SqlConnection(_connectionString);
            var affectedRows = await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new
                {
                    TenantId = tenantId,
                    SessionId = sessionId,
                    Resolution = resolution.ToString(),
                    ResolvedBy = resolvedBy,
                    ResolvedAt = resolvedAt,
                    PendingReview = HitlSessionStatus.PendingReview.ToString()
                },
                cancellationToken: cancellationToken));

            return affectedRows == 1;
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

        public async Task<bool> TryInvalidateAsync(
            string tenantId,
            Guid sessionId,
            string invalidationReason,
            CancellationToken cancellationToken = default)
        {
            const string sql = """
                UPDATE dbo.HitlSessions
                SET Status = @Invalidated,
                    ProcessingStartedAt = NULL,
                    InvalidationReason = @InvalidationReason
                WHERE TenantId = @TenantId AND SessionId = @SessionId AND Status = @Processing;
                """;

            await using var connection = new SqlConnection(_connectionString);
            var affectedRows = await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new
                {
                    TenantId = tenantId,
                    SessionId = sessionId,
                    InvalidationReason = invalidationReason,
                    Invalidated = HitlSessionStatus.Invalidated.ToString(),
                    Processing = HitlSessionStatus.Processing.ToString()
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
                       INSERTED.SerializedContext, INSERTED.Status, INSERTED.SuspendedAt,
                       INSERTED.ExecutionAttempts, INSERTED.ProcessingStartedAt,
                       INSERTED.ResolvedBy, INSERTED.ResolvedAt, INSERTED.InvalidationReason;
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

            return row?.ToEntity();
        }

        private sealed class HitlSessionRow
        {
            public Guid SessionId { get; init; }
            public string TenantId { get; init; } = string.Empty;
            public string Reason { get; init; } = string.Empty;
            public string SerializedContext { get; init; } = string.Empty;
            public string Status { get; init; } = string.Empty;
            public DateTimeOffset SuspendedAt { get; init; }
            public int ExecutionAttempts { get; init; }
            public DateTimeOffset? ProcessingStartedAt { get; init; }
            public string? ResolvedBy { get; init; }
            public DateTimeOffset? ResolvedAt { get; init; }
            public string? InvalidationReason { get; init; }

            public HitlSession ToEntity() => new(
                SessionId,
                TenantId,
                Reason,
                SerializedContext,
                Enum.Parse<HitlSessionStatus>(Status),
                SuspendedAt,
                ExecutionAttempts,
                ProcessingStartedAt,
                ResolvedBy,
                ResolvedAt,
                InvalidationReason);
        }
    }
}
