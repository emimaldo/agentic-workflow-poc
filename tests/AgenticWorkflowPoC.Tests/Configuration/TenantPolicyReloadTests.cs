using System;
using System.Collections.Generic;
using AgenticWorkflowPoC.Api.Configuration;
using AgenticWorkflowPoC.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgenticWorkflowPoC.Tests.Configuration
{
    public class TenantPolicyReloadTests
    {
        [Fact]
        public void PolicyProvider_ReflectsConfigurationReloadWithoutRestart()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TenantPolicies:tenant-a:OperatingAccountId"] = "OPS-EUR-001",
                    ["TenantPolicies:tenant-a:MaxPerTransfer"] = "100000",
                    ["TenantPolicies:tenant-a:DailyLimit"] = "60000",
                    ["TenantPolicies:tenant-a:AllowedCurrencies:0"] = "EUR"
                })
                .Build();
            using var services = new ServiceCollection().AddTenantPolicies(configuration).BuildServiceProvider();
            var provider = services.GetRequiredService<ITenantPolicyProvider>();
            Assert.Equal(100_000m, provider.GetRequiredPolicy("tenant-a").MaxPerTransfer);

            // An operator tightens the limit while a HITL session is waiting for approval.
            configuration["TenantPolicies:tenant-a:MaxPerTransfer"] = "5000";
            configuration.Reload();

            Assert.Equal(5_000m, provider.GetRequiredPolicy("tenant-a").MaxPerTransfer);
        }

        [Fact]
        public void PolicyProvider_TenantRemovedFromConfiguration_FailsClosedAfterReload()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TenantPolicies:tenant-a:OperatingAccountId"] = "OPS-EUR-001"
                })
                .Build();
            using var services = new ServiceCollection().AddTenantPolicies(configuration).BuildServiceProvider();
            var provider = services.GetRequiredService<ITenantPolicyProvider>();
            Assert.NotNull(provider.GetRequiredPolicy("tenant-a"));

            configuration["TenantPolicies:tenant-a:OperatingAccountId"] = null;
            configuration.Reload();

            Assert.Throws<InvalidOperationException>(() => provider.GetRequiredPolicy("tenant-a"));
        }
    }
}
