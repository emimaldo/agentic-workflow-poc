using System;
using System.Collections.Generic;
using System.Text.Json;
using AgenticWorkflowPoC.Api.Development;
using AgenticWorkflowPoC.Api.Security;
using AgenticWorkflowPoC.Api.Services;
using AgenticWorkflowPoC.Core.Entities;
using AgenticWorkflowPoC.Core.Interfaces;
using AgenticWorkflowPoC.Plugins;
using AgenticWorkflowPoC.Plugins.Financial;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgenticWorkflowPoC.Tests.Support
{
    public sealed class FixedTenantAccessor(string tenantId) : ITenantAccessor
    {
        public string TenantId { get; set; } = tenantId;

        public string SubjectId { get; set; } = "operator@" + tenantId;

        public string GetCurrentTenantId() => TenantId;

        public string GetCurrentSubjectId() => SubjectId;
    }

    public sealed class StaticTenantPolicyProvider : ITenantPolicyProvider
    {
        public Dictionary<string, TenantPolicy> Policies { get; } = new(StringComparer.Ordinal);

        public TenantPolicy GetRequiredPolicy(string tenantId) =>
            Policies.TryGetValue(tenantId, out var policy)
                ? policy
                : throw new InvalidOperationException("No financial policy is configured for the current tenant.");
    }

    public sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>
    /// Zero-token test harness: deterministic model, in-memory ledger and HITL store, fixed clock.
    /// </summary>
    public sealed class TransferScenario
    {
        public const string TenantA = "tenant-a";
        public const string TenantB = "tenant-b";
        public const string OperatingAccount = "OPS-EUR-001";
        public const decimal OpeningBalance = 50_000m;

        public TransferScenario(FakeChatClient? chat = null, string tenantId = TenantA)
        {
            Chat = chat ?? FakeChatClient.LowValueTransfer();
            Tenant = new FixedTenantAccessor(tenantId);
            Policies.Policies[TenantA] = DefaultPolicy();
            Policies.Policies[TenantB] = DefaultPolicy();
            Ledger
                .SeedAccount(TenantA, OperatingAccount, "EUR", OpeningBalance)
                .SeedAccount(TenantB, OperatingAccount, "EUR", OpeningBalance);
        }

        public FakeChatClient Chat { get; }
        public InMemoryLedger Ledger { get; } = new();
        public InMemoryHitlSessionRepository Repository { get; } = new();
        public HitlStateService Hitl { get; } = new();
        public ManualTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero));
        public StaticTenantPolicyProvider Policies { get; } = new();
        public FixedTenantAccessor Tenant { get; }

        public TenantPolicy PolicyA => Policies.Policies[TenantA];

        public static TenantPolicy DefaultPolicy() => new()
        {
            OperatingAccountId = OperatingAccount,
            HitlThreshold = 10_000m,
            MaxPerTransfer = 100_000m,
            DailyLimit = 60_000m,
            AllowedCurrencies = ["EUR", "USD"]
        };

        public FinancialTransferPlugin CreatePlugin(IHitlState? hitl = null, ITransferExecutor? executor = null) =>
            new(
                NullLogger<FinancialTransferPlugin>.Instance,
                hitl ?? Hitl,
                Ledger,
                executor ?? Ledger,
                Policies,
                Time);

        public TransferExtractionValidator CreateValidator() =>
            new(Chat.AsAIAgent(instructions: AgentDefaults.ExtractionPrompt), Tenant, Policies);

        public AgentService CreateAgentService() =>
            new(
                CreateValidator(),
                CreatePlugin(),
                Hitl,
                Repository,
                Tenant,
                Time,
                NullLogger<AgentService>.Instance);

        public HitlWorkProcessor CreateProcessor(ITransferExecutor? executor = null) =>
            new(
                Repository,
                CreatePlugin(new HitlStateService(), executor),
                Time,
                NullLogger<HitlWorkProcessor>.Instance);

        public static EnrichedTransferCommand Command(
            decimal amount,
            string tenantId = TenantA,
            string currency = "EUR",
            string destination = FakeChatClient.DestinationIban) => new()
        {
            DestinationAccount = destination,
            Amount = amount,
            Currency = currency,
            TenantId = tenantId,
            SourceAccountId = OperatingAccount
        };

        public HitlSession PendingSession(
            Guid sessionId,
            decimal amount = 12_500m,
            string tenantId = TenantA,
            HitlSessionStatus status = HitlSessionStatus.PendingReview) =>
            new(
                sessionId,
                tenantId,
                "Requires human review.",
                JsonSerializer.Serialize(Command(amount, tenantId)),
                status,
                Time.GetUtcNow());
    }
}
