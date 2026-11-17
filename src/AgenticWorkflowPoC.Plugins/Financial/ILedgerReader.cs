namespace AgenticWorkflowPoC.Plugins.Financial
{
    public readonly record struct LedgerQuery(
        string TenantId,
        string SourceAccountId,
        string DestinationAccount,
        Guid OperationId,
        DateTimeOffset AsOfUtc);

    /// <summary>
    /// Point-in-time view of the ledger used for policy evaluation.
    /// It is advisory: the executor re-checks balance, freeze and daily limit atomically.
    /// </summary>
    public sealed record LedgerSnapshot(
        bool AccountExists,
        string? AccountCurrency,
        decimal Balance,
        bool IsFrozen,
        decimal DebitedToday,
        bool BeneficiaryBlocked,
        bool OperationAlreadyExecuted);

    public interface ILedgerReader
    {
        Task<LedgerSnapshot> GetSnapshotAsync(LedgerQuery query, CancellationToken cancellationToken = default);
    }
}
