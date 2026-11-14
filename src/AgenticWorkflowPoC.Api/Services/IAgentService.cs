using System.Threading.Tasks;
using System;
using System.Threading;
using AgenticWorkflowPoC.Api.Models;

namespace AgenticWorkflowPoC.Api.Services
{
    public interface IAgentService
    {
        Task<AgentResponse> InvokeAsync(
            Guid sessionId,
            string tenantId,
            string prompt,
            CancellationToken cancellationToken = default);
        Task<AgentResponse> ResumeAsync(
            Guid sessionId,
            string tenantId,
            bool isApproved,
            CancellationToken cancellationToken = default);
    }
}
