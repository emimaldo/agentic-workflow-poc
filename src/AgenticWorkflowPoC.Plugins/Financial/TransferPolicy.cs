using AgenticWorkflowPoC.Core.Entities;

namespace AgenticWorkflowPoC.Plugins.Financial
{
    public enum TransferRejectionCode
    {
        CurrencyNotAllowed,
        ExceedsPerTransferLimit,
        AccountNotFound,
        AccountFrozen,
        CurrencyMismatch,
        BeneficiaryBlocked,
        InsufficientFunds,
        DailyLimitExceeded,
        OperationIdReused
    }

    public sealed record PolicyViolation(TransferRejectionCode Code, string Message);

    /// <summary>
    /// Pure, deterministic financial rules. Shared by the initial evaluation and by the
    /// Bounded Re-evaluation performed before an approved transfer executes.
    /// </summary>
    public static class TransferPolicy
    {
        public static PolicyViolation? EvaluateTenantLimits(TransferRequestPayload request, TenantPolicy policy)
        {
            if (!policy.AllowedCurrencies.Contains(request.Currency, StringComparer.Ordinal))
            {
                return new(TransferRejectionCode.CurrencyNotAllowed, $"Currency {request.Currency} is not enabled for this tenant.");
            }

            if (request.Amount > policy.MaxPerTransfer)
            {
                return new(TransferRejectionCode.ExceedsPerTransferLimit, "The amount exceeds the per-transfer limit for this tenant.");
            }

            return null;
        }

        public static PolicyViolation? EvaluateLedger(TransferRequestPayload request, LedgerSnapshot snapshot, TenantPolicy policy)
        {
            if (!snapshot.AccountExists)
            {
                return new(TransferRejectionCode.AccountNotFound, "The tenant operating account does not exist.");
            }

            if (snapshot.IsFrozen)
            {
                return new(TransferRejectionCode.AccountFrozen, "The tenant operating account is frozen.");
            }

            if (!string.Equals(snapshot.AccountCurrency, request.Currency, StringComparison.Ordinal))
            {
                return new(TransferRejectionCode.CurrencyMismatch, "The transfer currency does not match the operating account currency.");
            }

            if (snapshot.BeneficiaryBlocked)
            {
                return new(TransferRejectionCode.BeneficiaryBlocked, "The beneficiary account is blocked for this tenant.");
            }

            if (snapshot.Balance < request.Amount)
            {
                return new(TransferRejectionCode.InsufficientFunds, "The operating account has insufficient funds.");
            }

            if (snapshot.DebitedToday + request.Amount > policy.DailyLimit)
            {
                return new(TransferRejectionCode.DailyLimitExceeded, "The transfer would exceed the tenant daily limit.");
            }

            return null;
        }

        public static bool RequiresHumanApproval(TransferRequestPayload request, TenantPolicy policy) =>
            request.Amount > policy.HitlThreshold;

        /// <summary>Masks an account identifier for logs and user-facing messages (PII).</summary>
        public static string Mask(string account) =>
            account.Length <= 4 ? "****" : $"****{account[^4..]}";
    }
}
