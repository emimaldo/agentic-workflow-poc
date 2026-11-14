using AgenticWorkflowPoC.Core.Entities;

namespace AgenticWorkflowPoC.Plugins.Financial
{
    public sealed record TransferExecutionRequest(
        Guid OperationId,
        EnrichedTransferCommand Command,
        decimal DailyLimit,
        DateTimeOffset AsOfUtc);

    public enum TransferExecutionStatus
    {
        Executed,
        AlreadyExecuted,
        AccountNotFound,
        AccountFrozen,
        CurrencyMismatch,
        InsufficientFunds,
        DailyLimitExceeded
    }

    /// <summary>
    /// Debits the source account and records the transfer in a single atomic unit of work.
    /// Idempotent by <see cref="TransferExecutionRequest.OperationId"/>: replaying the same operation
    /// returns <see cref="TransferExecutionStatus.AlreadyExecuted"/>; reusing the key for a different
    /// payload throws <see cref="InvalidOperationException"/>.
    /// </summary>
    public interface ITransferExecutor
    {
        Task<TransferExecutionStatus> ExecuteAsync(TransferExecutionRequest request, CancellationToken cancellationToken = default);
    }
}
