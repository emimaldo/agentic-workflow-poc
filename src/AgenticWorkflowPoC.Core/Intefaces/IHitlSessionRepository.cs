using System;
using System.Threading;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Core.Entities;

namespace AgenticWorkflowPoC.Core.Interfaces
{
    public interface IHitlSessionRepository
    {
        Task<HitlSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default);
        Task CreatePendingAsync(HitlSession session, CancellationToken cancellationToken = default);
        Task<bool> TryTransitionAsync(
            Guid sessionId,
            HitlSessionStatus expectedStatus,
            HitlSessionStatus newStatus,
            CancellationToken cancellationToken = default);
    }
}
