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
        DailyLimitExceeded,

        /// <summary>The operation id was already used for a different transfer; nothing was executed.</summary>
        IdempotencyConflict
    }

    /// <summary>
    /// Debits the source account and records the transfer in a single atomic unit of work.
    /// Idempotent by <see cref="TransferExecutionRequest.OperationId"/>: replaying the same operation
    /// returns <see cref="TransferExecutionStatus.AlreadyExecuted"/>; reusing the key for a different
    /// payload returns <see cref="TransferExecutionStatus.IdempotencyConflict"/> without side effects.
    /// </summary>
    public interface ITransferExecutor
    {
        Task<TransferExecutionStatus> ExecuteAsync(TransferExecutionRequest request, CancellationToken cancellationToken = default);
    }
}
