using AgenticWorkflowPoC.Core.Entities;

namespace AgenticWorkflowPoC.Core.Interfaces
{
    public interface ITenantPolicyProvider
    {
        /// <summary>
        /// Returns the policy for <paramref name="tenantId"/>.
        /// Throws <see cref="System.InvalidOperationException"/> when the tenant is not configured (fail-closed).
        /// </summary>
        TenantPolicy GetRequiredPolicy(string tenantId);
    }
}
