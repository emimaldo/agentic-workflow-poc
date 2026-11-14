using System;
using System.Threading;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Core.Entities;

namespace AgenticWorkflowPoC.Core.Interfaces
{
    public interface IHitlSessionRepository
    {
        Task<HitlSession?> GetAsync(string tenantId, Guid sessionId, CancellationToken cancellationToken = default);
        Task CreatePendingAsync(HitlSession session, CancellationToken cancellationToken = default);
        Task<bool> TryTransitionAsync(
            string tenantId,
            Guid sessionId,
            HitlSessionStatus expectedStatus,
            HitlSessionStatus newStatus,
            CancellationToken cancellationToken = default);
        Task<HitlSession?> TryClaimNextApprovedAsync(
            DateTimeOffset now,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken = default);
    }
}
