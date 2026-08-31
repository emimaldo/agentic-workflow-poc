namespace AgenticWorkflowPoC.Plugins
{
    public interface IHitlState
    {
        bool IsSuspended { get; set; }
        string Reason { get; set; }
    }
}
