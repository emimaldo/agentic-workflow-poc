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

        public Task<HitlSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _sessions.TryGetValue(sessionId, out var session);
            return Task.FromResult(session);
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
            Guid sessionId,
            HitlSessionStatus expectedStatus,
            HitlSessionStatus newStatus,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            while (_sessions.TryGetValue(sessionId, out var current))
            {
                if (current.Status != expectedStatus)
                {
                    return Task.FromResult(false);
                }

                var updated = current with { Status = newStatus };
                if (_sessions.TryUpdate(sessionId, updated, current))
                {
                    return Task.FromResult(true);
                }
            }

            return Task.FromResult(false);
        }
    }
}
