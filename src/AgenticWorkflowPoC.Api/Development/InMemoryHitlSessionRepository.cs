using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Core.Entities;
using AgenticWorkflowPoC.Core.Interfaces;

namespace AgenticWorkflowPoC.Api.Development
{
    public sealed class InMemoryHitlSessionRepository : IHitlSessionRepository
    {
        private readonly ConcurrentDictionary<Guid, HitlSession> _sessions = new();

        public Task<HitlSession?> GetAsync(string tenantId, Guid sessionId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _sessions.TryGetValue(sessionId, out var session);
            return Task.FromResult(session?.TenantId == tenantId ? session : null);
        }

        public Task CreatePendingAsync(HitlSession session, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_sessions.TryAdd(session.SessionId, session))
            {
                throw new InvalidOperationException($"HITL session {session.SessionId} already exists.");
            }

            return Task.CompletedTask;
        }

        public Task<bool> TryTransitionAsync(
            string tenantId,
            Guid sessionId,
            HitlSessionStatus expectedStatus,
            HitlSessionStatus newStatus,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            while (_sessions.TryGetValue(sessionId, out var current))
            {
                if (current.TenantId != tenantId || current.Status != expectedStatus)
                {
                    return Task.FromResult(false);
                }

                var updated = current with
                {
                    Status = newStatus,
                    ProcessingStartedAt = newStatus == HitlSessionStatus.Processing ? current.ProcessingStartedAt : null
                };
                if (_sessions.TryUpdate(sessionId, updated, current))
                {
                    return Task.FromResult(true);
                }
            }

            return Task.FromResult(false);
        }

        public Task<HitlSession?> TryClaimNextApprovedAsync(
            DateTimeOffset now,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var leaseCutoff = now - leaseDuration;

            foreach (var entry in _sessions)
            {
                var current = entry.Value;
                var canClaim = current.Status == HitlSessionStatus.Approved
                    || (current.Status == HitlSessionStatus.Processing
                        && current.ProcessingStartedAt <= leaseCutoff);
                if (!canClaim)
                {
                    continue;
                }

                var claimed = current with
                {
                    Status = HitlSessionStatus.Processing,
                    ExecutionAttempts = current.ExecutionAttempts + 1,
                    ProcessingStartedAt = now
                };

                if (_sessions.TryUpdate(entry.Key, claimed, current))
                {
                    return Task.FromResult<HitlSession?>(claimed);
                }
            }

            return Task.FromResult<HitlSession?>(null);
        }
    }
}
