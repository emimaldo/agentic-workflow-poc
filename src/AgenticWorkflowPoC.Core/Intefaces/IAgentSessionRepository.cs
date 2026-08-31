using System;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Core.Entities;

namespace AgenticWorkflowPoC.Core.Interfaces
{
    public interface IAgentSessionRepository
    {
        // Retrieves a session by its unique identifier
        Task<AgentSession?> GetSessionAsync( Guid sessionId );

        // Creates a new session or updates an existing one (Upsert operation)
        Task SaveSessionAsync( AgentSession session );

        // Updates only the status of an existing session (useful for state machine transitions)
        Task UpdateSessionStatusAsync( Guid sessionId, string newStatus );
    }
}