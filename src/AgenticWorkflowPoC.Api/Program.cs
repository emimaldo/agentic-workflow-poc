using AgenticWorkflowPoC.Api.Configuration;
using AgenticWorkflowPoC.Api.Security;
using AgenticWorkflowPoC.Api.Services;
using AgenticWorkflowPoC.Core.Interfaces;
using AgenticWorkflowPoC.Infrastructure.Persistence;
using AgenticWorkflowPoC.Plugins.Operations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder( args );

// 1. Base API configuration
// Use NewtonsoftJson to avoid System.Text.Json async PipeWriter issues in TestServer
builder.Services.AddControllers().AddNewtonsoftJson();
builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddSwaggerGen();

// 2. Infrastructure Registration (Placeholder for future implementation)
builder.Services.AddScoped<IAgentSessionRepository, SqlAgentSessionRepo>();
builder.Services.AddScoped<IHitlSessionRepository, SqlHitlSessionRepository>();
builder.Services.AddScoped<IStaffAvailabilityExecutor, SqlStaffAvailabilityExecutor>();
builder.Services.AddHostedService<HitlSessionWorker>();

builder.Services
    .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName,
        _ => { });
builder.Services.AddAuthorization();

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

// Development override: when USE_FAKE_CHAT=true the app will register a deterministic fake
// IChatClient so local runs and HTTP tests work without Ollama.
if (builder.Configuration["USE_FAKE_CHAT"] == "true")
{
    var returnConflict = builder.Configuration["FAKE_RETURN_CONFLICT"] != "false";
    builder.Services.RemoveAll<IChatClient>();
    builder.Services.AddSingleton<IChatClient>(
        new AgenticWorkflowPoC.Api.Development.FakeChatClient(returnConflict));
    builder.Services.RemoveAll<IHitlSessionRepository>();
    builder.Services.AddSingleton<IHitlSessionRepository, AgenticWorkflowPoC.Api.Development.InMemoryHitlSessionRepository>();
    builder.Services.RemoveAll<IStaffAvailabilityExecutor>();
    builder.Services.AddSingleton<IStaffAvailabilityExecutor, AgenticWorkflowPoC.Api.Development.InMemoryStaffAvailabilityExecutor>();
}

var app = builder.Build();

if( app.Environment.IsDevelopment() )
{
    //app.UseSwagger();
    //app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
