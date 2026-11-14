using System;
using System.Threading;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Api.Models;

namespace AgenticWorkflowPoC.Api.Services
{
    /// <summary>
    /// Deterministic router. The tenant is resolved from the authenticated context
    /// (<see cref="Security.ITenantAccessor"/>), never passed in by callers.
    /// </summary>
    public interface IAgentService
    {
        Task<RoutingOutcome> InvokeAsync(Guid sessionId, string prompt, CancellationToken cancellationToken = default);

        Task<ResumeOutcome> ResumeAsync(Guid sessionId, bool isApproved, CancellationToken cancellationToken = default);
    }
}
