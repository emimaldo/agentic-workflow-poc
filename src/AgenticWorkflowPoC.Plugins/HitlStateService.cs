namespace AgenticWorkflowPoC.Plugins
{
    public class HitlStateService : IHitlState
    {
        public bool IsSuspended { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}
