namespace AgenticWorkflowPoC.Plugins
{
    public class HitlStateService : IHitlState
    {
        public bool IsSuspended { get; private set; }
        public string Reason { get; private set; } = string.Empty;
        public object? Context { get; private set; }

        public void Suspend(string reason, object currentContext)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
            ArgumentNullException.ThrowIfNull(currentContext);

            IsSuspended = true;
            Reason = reason;
            Context = currentContext;
        }
    }
}
