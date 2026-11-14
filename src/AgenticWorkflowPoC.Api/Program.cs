using System.Globalization;
using AgenticWorkflowPoC.Api.Configuration;
using AgenticWorkflowPoC.Api.Development;
using AgenticWorkflowPoC.Api.Security;
using AgenticWorkflowPoC.Api.Services;
using AgenticWorkflowPoC.Core.Interfaces;
using AgenticWorkflowPoC.Infrastructure.Persistence;
using AgenticWorkflowPoC.Plugins.Financial;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder( args );

// 1. Base API configuration
// Use NewtonsoftJson to avoid System.Text.Json async PipeWriter issues in TestServer
builder.Services.AddControllers().AddNewtonsoftJson();
builder.Services.AddEndpointsApiExplorer();

// 2. Infrastructure (SQL Server via Dapper)
builder.Services.AddScoped<IAgentSessionRepository, SqlAgentSessionRepo>();
builder.Services.AddScoped<IHitlSessionRepository, SqlHitlSessionRepository>();
builder.Services.AddScoped<ILedgerReader, SqlLedgerReader>();
builder.Services.AddScoped<ITransferExecutor, SqlTransferExecutor>();
builder.Services.AddHostedService<HitlSessionWorker>();

builder.Services
    .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName,
        _ => { });
builder.Services.AddAuthorization(options =>
    options.AddPolicy(AuthorizationPolicies.RequireAdminRole, policy => policy.RequireRole("Admin")));

// Tenant context (Chapter 2): resolved only from the authenticated principal.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantAccessor, HttpContextTenantAccessor>();
builder.Services.AddOptions<TenantPolicyOptions>()
    .Configure<IConfiguration>((options, configuration) =>
        configuration.GetSection(TenantPolicyOptions.SectionName).Bind(options.Tenants));
builder.Services.AddSingleton<ITenantPolicyProvider, ConfigurationTenantPolicyProvider>();
builder.Services.AddSingleton(TimeProvider.System);

// 3. Agent Framework setup (local Ollama by default)
string modelId = builder.Configuration["Ollama:Model"] ?? "llama3.1";
string endpoint = builder.Configuration["Ollama:Endpoint"] ?? "http://localhost:11434";

builder.Services.AddAgenticWorkflow(modelId, endpoint);

// Development override: when USE_FAKE_CHAT=true the app registers a deterministic fake IChatClient
// and in-memory HITL/ledger stores, so local runs and HTTP tests work without Ollama or SQL Server.
if (builder.Configuration["USE_FAKE_CHAT"] == "true")
{
    var highValue = builder.Configuration["FAKE_HIGH_VALUE_TRANSFER"] != "false";
    builder.Services.RemoveAll<IChatClient>();
    builder.Services.AddSingleton<IChatClient>(
        highValue ? FakeChatClient.HighValueTransfer() : FakeChatClient.LowValueTransfer());
    builder.Services.RemoveAll<IHitlSessionRepository>();
    builder.Services.AddSingleton<IHitlSessionRepository, InMemoryHitlSessionRepository>();

    var ledger = new InMemoryLedger();
    foreach (var account in builder.Configuration.GetSection("DevLedger:Accounts").GetChildren())
    {
        ledger.SeedAccount(
            account["TenantId"] ?? throw new InvalidOperationException("DevLedger account requires TenantId."),
            account["AccountId"] ?? throw new InvalidOperationException("DevLedger account requires AccountId."),
            account["Currency"] ?? throw new InvalidOperationException("DevLedger account requires Currency."),
            decimal.Parse(account["Balance"] ?? "0", CultureInfo.InvariantCulture));
    }

    builder.Services.RemoveAll<ILedgerReader>();
    builder.Services.RemoveAll<ITransferExecutor>();
    builder.Services.AddSingleton(ledger);
    builder.Services.AddSingleton<ILedgerReader>(sp => sp.GetRequiredService<InMemoryLedger>());
    builder.Services.AddSingleton<ITransferExecutor>(sp => sp.GetRequiredService<InMemoryLedger>());
}

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
