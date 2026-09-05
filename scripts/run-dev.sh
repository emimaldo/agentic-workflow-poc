#!/usr/bin/env bash
set -euo pipefail

# Development helper: start the API with a deterministic fake chat service.
# Do NOT put secrets in this script; use environment variables when needed.

# Use fake chat by default for local development
export USE_FAKE_CHAT=${USE_FAKE_CHAT:-true}
export FAKE_RETURN_CONFLICT=${FAKE_RETURN_CONFLICT:-true}

dotnet run --project src/AgenticWorkflowPoC.Api --urls http://localhost:5002
