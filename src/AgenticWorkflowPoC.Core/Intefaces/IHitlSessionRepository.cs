using System;
using System.Threading;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Core.Entities;

namespace AgenticWorkflowPoC.Core.Interfaces
{
    public interface IHitlSessionRepository
    {
        Task<HitlSession?> GetAsync(string tenantId, Guid sessionId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Persists a new PendingReview session. Returns false when the tenant already has a session
        /// with this id (the primary key is (TenantId, SessionId), so ids never collide across tenants).
        /// </summary>
        Task<bool> TryCreatePendingAsync(HitlSession session, CancellationToken cancellationToken = default);

        /// <summary>
        /// Atomically records an operator decision (PendingReview → Approved | Rejected) with audit data.
        /// Returns false when the session is not pending for this tenant.
        /// </summary>
        Task<bool> TryResolveAsync(
            string tenantId,
            Guid sessionId,
            HitlSessionStatus resolution,
            string resolvedBy,
            DateTimeOffset resolvedAt,
            CancellationToken cancellationToken = default);

        Task<bool> TryTransitionAsync(
            string tenantId,
            Guid sessionId,
            HitlSessionStatus expectedStatus,
            HitlSessionStatus newStatus,
            CancellationToken cancellationToken = default);

        /// <summary>Processing → Invalidated, recording why the fresh domain check failed.</summary>
        Task<bool> TryInvalidateAsync(
            string tenantId,
            Guid sessionId,
            string invalidationReason,
            CancellationToken cancellationToken = default);

        Task<HitlSession?> TryClaimNextApprovedAsync(
            DateTimeOffset now,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken = default);
    }
}
