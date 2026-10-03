namespace AgenticWorkflowPoC.Plugins
{
    public interface IHitlState
    {
        bool IsSuspended { get; }
        string Reason { get; }
        void Suspend(string reason);
    }
}
