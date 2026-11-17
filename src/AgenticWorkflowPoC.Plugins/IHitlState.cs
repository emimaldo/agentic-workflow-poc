namespace AgenticWorkflowPoC.Plugins
{
    /// <summary>
    /// Request-scoped HITL circuit breaker (Chapter 3). Domain plugins mutate it instead of
    /// throwing; the orchestrator intercepts it, persists <see cref="Context"/> and returns HTTP 202.
    /// </summary>
    public interface IHitlState
    {
        bool IsSuspended { get; }
        string Reason { get; }

        /// <summary>The validated domain context that must be persisted for later resumption.</summary>
        object? Context { get; }

        void Suspend(string reason, object currentContext);
    }
}
