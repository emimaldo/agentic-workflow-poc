using System;

namespace AgenticWorkflowPoC.Core.Entities
{
    public enum HitlSessionStatus
    {
        PendingReview,
        Approved,
        Rejected,
        Completed,
        Failed
    }

    public sealed record HitlSession(
        Guid SessionId,
        string Reason,
        string PendingActionPayload,
        HitlSessionStatus Status,
        DateTimeOffset SuspendedAt);

    public sealed record PendingStaffOverride(string StaffId, DateTimeOffset NewAvailability);
}
