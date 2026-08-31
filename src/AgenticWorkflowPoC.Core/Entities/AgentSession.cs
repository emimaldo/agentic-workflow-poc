using System;

namespace AgenticWorkflowPoC.Core.Entities
{
    public class HitlState
    {
        public bool IsSuspended { get; set; } = false;
        public string SuspensionReason { get; set; } = string.Empty;
    }

    public class AgentSession
    {
        public Guid SessionId { get; set; }
        public string UserId { get; set; } = string.Empty;

        // Status can be: "Active", "Suspended_HITL", "Completed", "Canceled"
        public string Status { get; set; } = string.Empty;

        // The serialized ChatHistory from Semantic Kernel
        public string ChatHistoryJson { get; set; } = string.Empty;

        // Optional payload if the agent was trying to execute a specific action before pausing
        public string? PendingActionPayload { get; set; }

        public DateTime LastUpdated { get; set; }
    }
}