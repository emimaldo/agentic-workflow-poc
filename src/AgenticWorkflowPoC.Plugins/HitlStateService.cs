namespace AgenticWorkflowPoC.Plugins
{
    public class HitlStateService : IHitlState
    {
        public bool IsSuspended { get; private set; }
        public string Reason { get; private set; } = string.Empty;

        public void Suspend(string reason)
        {
            IsSuspended = true;
            Reason = reason;
        }
    }
}
