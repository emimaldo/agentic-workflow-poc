using System;

namespace AgenticWorkflowPoC.Api.Models
{
    public record AgentRequest(Guid SessionId, string Prompt);
    public record ResumeSessionRequest(bool IsApproved);

    /// <summary>Response body for 200/202 outcomes. Never contains the serialized context.</summary>
    public sealed record SessionResponse(Guid SessionId, string Status, string Message);

    /// <summary>Operator/client view of a HITL session. Deliberately excludes the serialized context.</summary>
    public sealed record HitlSessionView(
        Guid SessionId,
        string Status,
        string Reason,
        DateTimeOffset SuspendedAt,
        DateTimeOffset? ResolvedAt,
        string? InvalidationReason);
}
