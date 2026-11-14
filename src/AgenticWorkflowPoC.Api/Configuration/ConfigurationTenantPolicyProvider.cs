using System;
using System.Collections.Generic;
using AgenticWorkflowPoC.Core.Entities;
using AgenticWorkflowPoC.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace AgenticWorkflowPoC.Api.Configuration
{
    /// <summary>
    /// Bound from the <c>TenantPolicies</c> configuration section, keyed by TenantId:
    /// <c>TenantPolicies:{tenantId}:OperatingAccountId</c>, <c>TenantPolicies:{tenantId}:DailyLimit</c>, ...
    /// </summary>
    public sealed class TenantPolicyOptions
    {
        public const string SectionName = "TenantPolicies";

        public Dictionary<string, TenantPolicy> Tenants { get; } = new(StringComparer.Ordinal);
    }

    public sealed class ConfigurationTenantPolicyProvider : ITenantPolicyProvider
    {
        private readonly IOptionsMonitor<TenantPolicyOptions> _options;

        public ConfigurationTenantPolicyProvider(IOptionsMonitor<TenantPolicyOptions> options)
        {
            _options = options;
        }

        public TenantPolicy GetRequiredPolicy(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId)
                || !_options.CurrentValue.Tenants.TryGetValue(tenantId, out var policy)
                || string.IsNullOrWhiteSpace(policy.OperatingAccountId))
            {
                // Fail-closed: a tenant without an explicit policy can never move money.
                throw new InvalidOperationException("No financial policy is configured for the current tenant.");
            }

            return policy;
        }
    }
}
