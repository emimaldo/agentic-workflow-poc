using System.Collections.Generic;
using System.Net.Http;
using AgenticWorkflowPoC.Api.Development;
using AgenticWorkflowPoC.Api.Security;
using AgenticWorkflowPoC.Core.Interfaces;
using AgenticWorkflowPoC.Plugins.Financial;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgenticWorkflowPoC.Tests.Support
{
    /// <summary>
    /// Builds a zero-token API host: fake model, in-memory HITL store and ledger, API keys for two tenants.
    /// </summary>
    public static class ApiFactory
    {
        public const string AdminKeyA = "tenant-a-admin-key";
        public const string UserKeyA = "tenant-a-user-key";
        public const string AdminKeyB = "tenant-b-admin-key";

        public static WebApplicationFactory<AgenticWorkflowPoC.Api.Program> Create(
            WebApplicationFactory<AgenticWorkflowPoC.Api.Program> factory,
            TransferScenario scenario,
            IDictionary<string, string?>? extraConfiguration = null) =>
            factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    var settings = new Dictionary<string, string?>
                    {
                        ["USE_FAKE_CHAT"] = "true",
                        // Long sweep: E2E tests prove the resume signal wakes the worker immediately.
                        ["HitlWorker:SweepInterval"] = "00:01:00",
                        ["Authentication:ApiKeys:0:Key"] = AdminKeyA,
                        ["Authentication:ApiKeys:0:TenantId"] = TransferScenario.TenantA,
                        ["Authentication:ApiKeys:0:Role"] = "Admin",
                        ["Authentication:ApiKeys:1:Key"] = UserKeyA,
                        ["Authentication:ApiKeys:1:TenantId"] = TransferScenario.TenantA,
                        ["Authentication:ApiKeys:1:Role"] = "User",
                        ["Authentication:ApiKeys:2:Key"] = AdminKeyB,
                        ["Authentication:ApiKeys:2:TenantId"] = TransferScenario.TenantB,
                        ["Authentication:ApiKeys:2:Role"] = "Admin"
                    };

                    foreach (var tenant in new[] { TransferScenario.TenantA, TransferScenario.TenantB })
                    {
                        var prefix = $"TenantPolicies:{tenant}:";
                        settings[prefix + "OperatingAccountId"] = TransferScenario.OperatingAccount;
                        settings[prefix + "HitlThreshold"] = "10000";
                        settings[prefix + "MaxPerTransfer"] = "100000";
                        settings[prefix + "DailyLimit"] = "60000";
                        settings[prefix + "AllowedCurrencies:0"] = "EUR";
                    }

                    if (extraConfiguration is not null)
                    {
                        foreach (var (key, value) in extraConfiguration)
                        {
                            settings[key] = value;
                        }
                    }

                    configuration.AddInMemoryCollection(settings);
                });

                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IChatClient>();
                    services.AddSingleton<IChatClient>(scenario.Chat);
                    services.RemoveAll<IHitlSessionRepository>();
                    services.AddSingleton<IHitlSessionRepository>(scenario.Repository);
                    services.RemoveAll<InMemoryLedger>();
                    services.RemoveAll<ILedgerReader>();
                    services.RemoveAll<ITransferExecutor>();
                    services.AddSingleton<ILedgerReader>(scenario.Ledger);
                    services.AddSingleton<ITransferExecutor>(scenario.Ledger);
                });
            });

        public static HttpClient CreateClient(WebApplicationFactory<AgenticWorkflowPoC.Api.Program> factory, string? apiKey)
        {
            var client = factory.CreateClient();
            if (apiKey is not null)
            {
                client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.ApiKeyHeaderName, apiKey);
            }

            return client;
        }
    }
}
