# CLAUDE.md — Architectural & Coding Standards

This repository is the official companion Proof of Concept (PoC) for the technical book **"Building Enterprise Agentic Workflows in .NET"** by Emiliano Maldonado.

Every class, endpoint, test, and SQL script generated in this repository must strictly mirror the architectural patterns defined in the book and serve as production-grade reference code.

## 1. Core Architectural Philosophy
- **"LLM as a Pure Semantic Parser, C# as the Single Source of Truth":** Never grant autonomous execution loops to the LLM. `ChatToolMode.Auto` is strictly forbidden.
- **Zero-Trust Execution:** The LLM extracts structured intent via `ChatResponseFormat.ForJsonSchema<T>()`. Deterministic C# code owns authentication, multi-tenant context (`TenantId`), validation, routing, state persistence, and external tool execution.

## 2. Stack & Version Constraints
- **Target Framework:** Strictly `.NET 10` (`net10.0` LTS) and `C# 14`. Do NOT use `.NET 11` / `C# 15` features (e.g., native `union` types or async DataAnnotations) to maintain strict parity with the manuscript and enterprise LTS standards.
- **AI Abstractions:** `Microsoft.Extensions.AI` (`IChatClient`, `DelegatingChatClient`) and `Microsoft Agent Framework (MAF v1.28+)`.
- **Forbidden Legacy Libraries:** Never use `Semantic Kernel` (`Kernel`, `[KernelFunction]`) or archived `AutoGen` patterns.
- **Database:** SQL Server (pure T-SQL) accessed via `Dapper` and `IDbConnectionFactory` (no Entity Framework Core in hot/state persistence paths).

## 3. Mandatory Architectural Patterns
1. **Deterministic Routing & Validation:**
   - Every LLM extraction must pass through `IExtractionValidator<T>` (checking schema, domain invariants, grounding, and tenant limits) before any routing decision.
   - Closed outcome routing resolves strictly to: immediate execution (`HTTP 200`), Human-in-the-Loop suspension (`HTTP 202 Accepted`), or domain rejection (`HTTP 422`).
2. **Out-of-Band HITL Suspension & Bounded Re-evaluation:**
   - High-risk operations persist an immutable `SerializedContext` to SQL Server and immediately release the HTTP thread with `HTTP 202 Accepted` + `Location` header.
   - External `/resume` signals are treated as hints, never blind authorizations: atomically persist approval state in SQL first, enqueue into `System.Threading.Channels`, and perform a fresh domain state check (*Bounded Re-evaluation*) in the worker before executing.
3. **High-Concurrency State Offload & Persistence:**
   - Offload state mutations via bounded `System.Threading.Channels` (`BoundedChannelFullMode.Wait`) using immutable `readonly record struct` payloads.
   - Drain via a `BackgroundService` executing atomic T-SQL `UPSERT` statements (`SET TRANSACTION ISOLATION LEVEL SERIALIZABLE` + `WITH (UPDLOCK, HOLDLOCK)`) with generation/timestamp fencing, `Polly` resilience pipelines, and a Dead Letter Queue (DLQ) fallback.
4. **Stateless Multi-Tenant MCP & Fail-Closed Security (MAF v1.28+ Conventions):**
   - Never cache initialized MCP client sessions across HTTP requests or HITL pauses. Resolve `TenantId` from the authenticated C# HTTP context and inject per-request credentials dynamically.
   - Enforce explicit host/port allowlists on outbound HTTP handlers (`AllowAutoRedirect = false`, `UseCookies = false`) and redact internal exceptions (`IncludeDetailedErrors = false`).
5. **Externalized Prompt Governance & Semantic Observability:**
   - Prompts live in a versioned SQL registry loaded via Cache-Aside (never hardcoded in C#).
   - Wrap `IChatClient` with `DelegatingChatClient` emitting OpenTelemetry GenAI standard attributes (`gen_ai.operation.name = "invoke_workflow"`, `gen_ai.request.model`, and custom prompt version tags). Never log raw prompt payloads or PII in trace spans.
6. **Zero-Token Integration Testing:**
   - Build deterministic integration tests using `FakeChatClient` (implementing `IChatClient`) to verify validation, `HTTP 202` suspension, channel backpressure, and SQL upserts without calling live LLM APIs.

## 4. Manuscript Errata (the code in this repository is the reference)
Discrepancies found while implementing Chapters 2–3. Fix them in the manuscript, not by bending the code.

| Chapter | Manuscript | Repository / correct API |
|---|---|---|
| 2, 5 | `chatClient.CompleteAsync(...)` / `ChatCompletion` | `GetResponseAsync(...)` / `ChatResponse` (MEAI GA) |
| 2 | `FunctionChoiceBehavior.Auto()` | Semantic Kernel term; MEAI uses `ChatToolMode` (here: `ChatToolMode.None`) |
| 2 | `ValidationResult<T>` next to `using System.ComponentModel.DataAnnotations` | Ambiguous with `DataAnnotations.ValidationResult` (does not compile); renamed `ExtractionResult<T>` |
| 3 | `FakeChatCompletionService` | `FakeChatClient : IChatClient` |
| 3 | Resume returns `Results.Ok(...)` | `202 Accepted` + `Location`: execution is asynchronous and happens after Bounded Re-evaluation |
| 3 | `IHitlState.Suspend(reason, context)` stores the context | Same; the persisted column is `SerializedContext` |
| 4 | Polly v7 `Policy.Handle<SqlException>().WaitAndRetryAsync` | Polly v8 `ResiliencePipeline` |
| 4 | `UPDATE ... WITH (UPDLOCK)` upsert | Needs `UPDLOCK, HOLDLOCK` under `SERIALIZABLE` to prevent the insert race |
| 4 | `AgentStatePayload.TenantId` is `Guid` | `TenantId` is a `string` everywhere else |
| 4 | — | **Add:** SQL Server keeps a session's isolation level when a pooled connection is reused. After a `SERIALIZABLE` transaction, reset to `READ COMMITTED` (or declare it per batch); otherwise `READPAST` queries fail and reads take range locks. Found by the Testcontainers suite. |
| 5 | `.UseDistributedCache()` on the extraction client | Caches responses across tenants unless the cache key includes the tenant; avoid on extraction |
| 6 | `WITH (NOLOCK)` on `dbo.PromptRegistry` | Dirty reads can activate an uncommitted prompt version; use `READ COMMITTED` (snapshot) |
| 7 | `ChatToolCallBehavior.RequireAny()` | `ChatToolMode.RequireAny` |
| 7 | `Microsoft.Agent.Framework.Mcp`, `IMcpClientFactory` | Not real APIs; use the official MCP C# SDK client types |

## 5. Reference Domain
Chapters 2–3 use a FinTech transfer scenario (`TransferRequestPayload` → `EnrichedTransferCommand`,
`FinancialTransferPlugin`, HITL above the tenant threshold). The source account and `TenantId` always come from the
authenticated tenant context and `TenantPolicies` configuration, never from the model.
