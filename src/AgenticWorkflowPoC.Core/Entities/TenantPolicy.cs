using System;

namespace AgenticWorkflowPoC.Core.Entities
{
    /// <summary>
    /// Per-tenant financial policy. Defaults are fail-closed: an unconfigured limit is zero,
    /// so every transfer is rejected until the tenant is explicitly configured.
    /// </summary>
    public sealed class TenantPolicy
    {
        /// <summary>Account debited for this tenant. Never supplied by the user or the model.</summary>
        public string OperatingAccountId { get; set; } = string.Empty;

        /// <summary>Amounts strictly above this value require Human-in-the-Loop approval.</summary>
        public decimal HitlThreshold { get; set; } = 10_000m;

        public decimal MaxPerTransfer { get; set; }

        public decimal DailyLimit { get; set; }

        public string[] AllowedCurrencies { get; set; } = [];
    }
}
