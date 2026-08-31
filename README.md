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