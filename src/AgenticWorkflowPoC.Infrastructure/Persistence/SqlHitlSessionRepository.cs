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

        public async Task<HitlSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default)
        {
            const string sql = """
                SELECT SessionId, Reason, PendingActionPayload, Status, SuspendedAt
                FROM dbo.HitlSessions
                WHERE SessionId = @SessionId;
                """;

            await using var connection = new SqlConnection(_connectionString);
            var row = await connection.QuerySingleOrDefaultAsync<HitlSessionRow>(
                new CommandDefinition(sql, new { SessionId = sessionId }, cancellationToken: cancellationToken));

            return row is null
                ? null
                : new HitlSession(
                    row.SessionId,
                    row.Reason,
                    row.PendingActionPayload,
                    Enum.Parse<HitlSessionStatus>(row.Status),
                    row.SuspendedAt);
        }

        public async Task CreatePendingAsync(HitlSession session, CancellationToken cancellationToken = default)
        {
            const string sql = """
                INSERT INTO dbo.HitlSessions (SessionId, Reason, PendingActionPayload, Status, SuspendedAt)
                VALUES (@SessionId, @Reason, @PendingActionPayload, @Status, @SuspendedAt);
                """;

            await using var connection = new SqlConnection(_connectionString);
            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new
                {
                    session.SessionId,
                    session.Reason,
                    session.PendingActionPayload,
                    Status = session.Status.ToString(),
                    session.SuspendedAt
                },
                cancellationToken: cancellationToken));
        }

        public async Task<bool> TryTransitionAsync(
            Guid sessionId,
            HitlSessionStatus expectedStatus,
            HitlSessionStatus newStatus,
            CancellationToken cancellationToken = default)
        {
            const string sql = """
                UPDATE dbo.HitlSessions
                SET Status = @NewStatus
                WHERE SessionId = @SessionId AND Status = @ExpectedStatus;
                """;

            await using var connection = new SqlConnection(_connectionString);
            var affectedRows = await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new
                {
                    SessionId = sessionId,
                    ExpectedStatus = expectedStatus.ToString(),
                    NewStatus = newStatus.ToString()
                },
                cancellationToken: cancellationToken));

            return affectedRows == 1;
        }

        private sealed class HitlSessionRow
        {
            public Guid SessionId { get; init; }
            public string Reason { get; init; } = string.Empty;
            public string PendingActionPayload { get; init; } = string.Empty;
            public string Status { get; init; } = string.Empty;
            public DateTimeOffset SuspendedAt { get; init; }
        }
    }
}
