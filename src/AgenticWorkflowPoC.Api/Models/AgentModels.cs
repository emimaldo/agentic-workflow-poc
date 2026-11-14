using System;

namespace AgenticWorkflowPoC.Api.Models
{
    public record AgentRequest(Guid SessionId, string Prompt);
    public record ResumeRequest(bool IsApproved);

    /// <summary>Response body for 200/202 outcomes. Never contains the serialized context.</summary>
    public sealed record SessionResponse(Guid SessionId, string Status, string Message);
}
