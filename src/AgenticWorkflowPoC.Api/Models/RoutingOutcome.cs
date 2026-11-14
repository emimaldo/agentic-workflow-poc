using System;
using System.Collections.Generic;
using AgenticWorkflowPoC.Core.Entities;

namespace AgenticWorkflowPoC.Api.Models
{
    public sealed record RoutingError(string Code, string Message);

    /// <summary>
    /// Closed set of routing decisions for an invocation (Chapters 2-3).
    /// The private constructor seals the hierarchy: no other outcome can exist,
    /// and the controller maps each one to exactly one HTTP status (200 / 202 / 422).
    /// (A closed record hierarchy instead of C# 15 unions, to stay on .NET 10 / C# 14.)
    /// </summary>
    public abstract record RoutingOutcome
    {
        private RoutingOutcome(Guid sessionId) => SessionId = sessionId;

        public Guid SessionId { get; }

        /// <summary>Executed immediately → HTTP 200.</summary>
        public sealed record Completed(Guid SessionId, string Message) : RoutingOutcome(SessionId);

        /// <summary>Persisted for Human-in-the-Loop review → HTTP 202 Accepted.</summary>
        public sealed record Suspended(Guid SessionId, string Reason) : RoutingOutcome(SessionId);

        /// <summary>Rejected by validation or domain rules → HTTP 422.</summary>
        public sealed record Rejected(Guid SessionId, IReadOnlyList<RoutingError> Errors) : RoutingOutcome(SessionId);
    }

    /// <summary>Closed set of outcomes for an operator's resume signal.</summary>
    public abstract record ResumeOutcome
    {
        private ResumeOutcome(Guid sessionId) => SessionId = sessionId;

        public Guid SessionId { get; }

        /// <summary>Approval persisted; execution happens in the background → HTTP 202.</summary>
        public sealed record Queued(Guid SessionId) : ResumeOutcome(SessionId);

        /// <summary>Operator rejected the operation → HTTP 200.</summary>
        public sealed record RejectedByOperator(Guid SessionId) : ResumeOutcome(SessionId);

        /// <summary>No session for this tenant → HTTP 404 (never 403: no cross-tenant enumeration).</summary>
        public sealed record NotFound(Guid SessionId) : ResumeOutcome(SessionId);

        /// <summary>Already resolved by another signal → HTTP 409.</summary>
        public sealed record AlreadyResolved(Guid SessionId, HitlSessionStatus CurrentStatus) : ResumeOutcome(SessionId);
    }
}
