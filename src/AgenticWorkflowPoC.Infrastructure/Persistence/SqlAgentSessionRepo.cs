using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Dapper;
using AgenticWorkflowPoC.Core.Entities;
using AgenticWorkflowPoC.Core.Interfaces;

namespace AgenticWorkflowPoC.Infrastructure.Persistence
{
    public class SqlAgentSessionRepo : IAgentSessionRepository
    {
        private readonly string _connectionString;

        public SqlAgentSessionRepo( IConfiguration configuration )
        {
            // Staff Note: Ensure the connection string points to a user with minimal privileges 
            // (e.g., only execute permissions on required stored procedures or CRUD on specific tables).
            _connectionString = configuration.GetConnectionString( "AgenticDatabase" )
                ?? throw new InvalidOperationException( "Database connection string is missing." );
        }

        public async Task<AgentSession?> GetSessionAsync( Guid sessionId )
        {
            const string sql = @"
                SELECT SessionId, UserId, Status, ChatHistoryJson, PendingActionPayload, LastUpdated 
                FROM dbo.AgentSessions 
                WHERE SessionId = @SessionId";

            using var connection = new SqlConnection( _connectionString );

            // Dapper automatically maps the SQL result columns to the AgentSession properties
            return await connection.QuerySingleOrDefaultAsync<AgentSession>( sql, new { SessionId = sessionId } );
        }

        public async Task SaveSessionAsync( AgentSession session )
        {
            // Implementation of an UPSERT (Update if exists, Insert if not)
            // Using standard SQL Server syntax (MERGE can be used, but this is often safer against deadlocks)
            const string sql = @"
                IF EXISTS (SELECT 1 FROM dbo.AgentSessions WHERE SessionId = @SessionId)
                BEGIN
                    UPDATE dbo.AgentSessions 
                    SET Status = @Status, 
                        ChatHistoryJson = @ChatHistoryJson, 
                        PendingActionPayload = @PendingActionPayload,
                        LastUpdated = GETUTCDATE()
                    WHERE SessionId = @SessionId
                END
                ELSE
                BEGIN
                    INSERT INTO dbo.AgentSessions (SessionId, UserId, Status, ChatHistoryJson, PendingActionPayload, LastUpdated)
                    VALUES (@SessionId, @UserId, @Status, @ChatHistoryJson, @PendingActionPayload, GETUTCDATE())
                END";

            using var connection = new SqlConnection( _connectionString );
            await connection.ExecuteAsync( sql, session );
        }

        public async Task UpdateSessionStatusAsync( Guid sessionId, string newStatus )
        {
            const string sql = @"
                UPDATE dbo.AgentSessions 
                SET Status = @Status, 
                    LastUpdated = GETUTCDATE()
                WHERE SessionId = @SessionId";

            using var connection = new SqlConnection( _connectionString );
            await connection.ExecuteAsync( sql, new { SessionId = sessionId, Status = newStatus } );
        }
    }
}
