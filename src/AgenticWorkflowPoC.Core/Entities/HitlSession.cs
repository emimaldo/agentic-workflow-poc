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
        Failed
    }

    public sealed record HitlSession(
        Guid SessionId,
        string TenantId,
        string Reason,
        string PendingActionPayload,
        HitlSessionStatus Status,
        DateTimeOffset SuspendedAt,
        int ExecutionAttempts = 0,
        DateTimeOffset? ProcessingStartedAt = null);
}
