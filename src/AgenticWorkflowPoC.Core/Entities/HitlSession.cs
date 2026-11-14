using System;

namespace AgenticWorkflowPoC.Core.Entities
{
    public enum HitlSessionStatus
    {
        PendingReview,
        Approved,
        Processing,
        Rejected,
        Completed,
        Failed,

        /// <summary>
        /// Approved, but the fresh domain check before execution failed (Bounded Re-evaluation).
        /// Terminal: nothing was executed.
        /// </summary>
        Invalidated
    }

    /// <summary>
    /// Suspended HITL session (Chapter 3). <see cref="SerializedContext"/> is the immutable, validated
    /// domain command captured at suspension time; it is never re-extracted by the LLM.
    /// </summary>
    public sealed record HitlSession(
        Guid SessionId,
        string TenantId,
        string Reason,
        string SerializedContext,
        HitlSessionStatus Status,
        DateTimeOffset SuspendedAt,
        int ExecutionAttempts = 0,
        DateTimeOffset? ProcessingStartedAt = null,
        string? ResolvedBy = null,
        DateTimeOffset? ResolvedAt = null,
        string? InvalidationReason = null);
}
