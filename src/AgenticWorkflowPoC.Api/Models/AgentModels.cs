using System;

namespace AgenticWorkflowPoC.Api.Models
{
    public record AgentRequest(Guid SessionId, string Prompt);
    public record ResumeRequest(bool IsApproved);
    public record AgentResponse(string Status, string? Message = null, string? SessionId = null, string? Response = null);
}
