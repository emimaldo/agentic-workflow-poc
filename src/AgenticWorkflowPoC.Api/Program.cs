using AgenticWorkflowPoC.Api.Configuration;
using AgenticWorkflowPoC.Core.Interfaces;
using AgenticWorkflowPoC.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder( args );

// 1. Base API configuration
// Use NewtonsoftJson to avoid System.Text.Json async PipeWriter issues in TestServer
builder.Services.AddControllers().AddNewtonsoftJson();
builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddSwaggerGen();

// 2. Infrastructure Registration (Placeholder for future implementation)
builder.Services.AddScoped<IAgentSessionRepository, SqlAgentSessionRepo>();

// 3. Agentic Ecosystem Setup (local Ollama by default)
string modelId = builder.Configuration["Ollama:Model"] ?? "llama3.1";
string endpoint = builder.Configuration["Ollama:Endpoint"] ?? "http://localhost:11434";

builder.Services.AddAgenticWorkflow( modelId, endpoint );

var app = builder.Build();

if( app.Environment.IsDevelopment() )
{
    //app.UseSwagger();
    //app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();

app.Run();
