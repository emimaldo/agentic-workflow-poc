# AgenticWorkflowPoC

Pequeño Proof-of-Concept que demuestra un flujo agente-determinista para operaciones internas.

Características principales
- API ASP.NET Core (net9.0) que orquesta llamadas a un LLM local (Ollama) vía Semantic Kernel.
- Enfoque determinista: el modelo devuelve solo JSON; el `AgentController` parsea el JSON y llama a plugins (no invocación automática de funciones).
- `IHitlState` request-scoped para manejar interacciones humano-en-el-bucle (HITL) sin estado global.
- Tests: unitarios y E2E usando `WebApplicationFactory` con un fake de `IChatCompletionService`.

Estructura
- `src/AgenticWorkflowPoC.Api` — API web y configuración del Kernel.
- `src/AgenticWorkflowPoC.Plugins` — plugins (ej. `StaffOverridesPlugin`) y `IHitlState`.
- `src/AgenticWorkflowPoC.Core` — entidades e interfaces.
- `src/AgenticWorkflowPoC.Infrastructure` — persistencia (esqueleto SQL).
- `tests/AgenticWorkflowPoC.Tests` — tests unitarios e integración.

Requisitos
- .NET 9 SDK
- (Opcional) Ollama local en `http://localhost:11434` con un modelo compatible (ej. `llama3.1`) si querés probar la integración real.

Comandos rápidos

Ejecutar tests:
```bash
dotnet test AgenticWorkflowPoC.sln
```

Levantar la API localmente:
```bash
dotnet run --project src/AgenticWorkflowPoC.Api
```

Usar Docker (solo SQL):
```bash
docker compose up -d
```

Notas de diseño
- Deterministic router: el controlador exige que el LLM responda únicamente con JSON estructurado, evita que el modelo halucine acciones.
- `IHitlState` es `Scoped` y se inyecta en plugins para mantener el estado por petición.

Cómo contribuir
- Ver [CONTRIBUTING.md](CONTRIBUTING.md) para proceso de PR, pruebas y estilo.

Licencia
- Este repositorio es un PoC; añadí licencia si querés publicar (no se incluye licencia por defecto).
# 🤖 Agentic Workflow POC: Deterministic AI with Human-in-the-Loop

This repository demonstrates a **Production-Ready Agentic Architecture** using .NET 8, Microsoft Semantic Kernel, and SQL Server. 

Unlike standard "chatbot" implementations, this architecture treats the Large Language Model (LLM) as an orchestrator for deterministic C# code. It showcases strict boundary enforcement, separation of concerns (Clean Architecture), and the **Human-in-the-Loop (HITL)** pattern for safely executing sensitive business mutations.

## 🏗️ Architecture Overview

The solution follows Clean Architecture principles to isolate the AI orchestrator from the underlying domain and infrastructure:

*   **`Core`**: Contains POCO entities (`AgentSession`) and Repository Interfaces. No dependencies on AI frameworks or databases.
*   **`Plugins`**: The application layer. Contains standard C# classes decorated with `[KernelFunction]`. These are the "tools" the agent uses to evaluate business rules (e.g., `StaffOverridesPlugin`).
*   **`Infrastructure`**: Highly optimized data access using **Dapper** and SQL Server to serialize and persist the state of the agent.
*   **`Api`**: The entry point. Manages the HTTP lifecycle, Dependency Injection, and hosts the Semantic Kernel orchestrator.

## ⏸️ The Human-in-the-Loop (HITL) Pattern

One of the biggest risks of Agentic AI is allowing models to execute destructive operations autonomously. This POC implements a state-machine suspension pattern:

1. **Invoke:** The LLM intends to mutate state (e.g., override a staff schedule).
2. **Evaluate:** The native C# Plugin executes strict business rules. If a conflict is detected, it returns a `HITL_PAUSE` signal instead of executing the action.
3. **Suspend:** The API Controller intercepts the signal, serializes the `ChatHistory` (the agent's brain), saves it to SQL Server via Dapper, and returns an HTTP 202 Accepted.
4. **Resume:** A human administrator reviews the conflict. A `/resume` endpoint is called with the decision, the memory is rehydrated from SQL, and the agent completes the transaction.

## 🚀 Getting Started

### 1. Prerequisites
* [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
* Docker Desktop
* An OpenAI API Key

### 2. Infrastructure Setup
Run the included docker-compose file to spin up SQL Server 2022:
```bash
docker-compose up -d