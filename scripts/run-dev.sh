#!/usr/bin/env bash
set -euo pipefail

# Development helper: start the API with a deterministic fake chat service.
# Do NOT put secrets in this script; use environment variables when needed.

# Use fake chat by default for local development
export USE_FAKE_CHAT=${USE_FAKE_CHAT:-true}
export FAKE_HIGH_VALUE_TRANSFER=${FAKE_HIGH_VALUE_TRANSFER:-true}

# Dev-only tenant policy and in-memory ledger seed (tenant ids contain '-', so they are passed
# as command-line configuration instead of environment variables).
dotnet run --project src/AgenticWorkflowPoC.Api --urls http://localhost:5002 -- \
  --TenantPolicies:tenant-local:OperatingAccountId=OPS-EUR-001 \
  --TenantPolicies:tenant-local:HitlThreshold=10000 \
  --TenantPolicies:tenant-local:MaxPerTransfer=100000 \
  --TenantPolicies:tenant-local:DailyLimit=60000 \
  --TenantPolicies:tenant-local:AllowedCurrencies:0=EUR \
  --DevLedger:Accounts:0:TenantId=tenant-local \
  --DevLedger:Accounts:0:AccountId=OPS-EUR-001 \
  --DevLedger:Accounts:0:Currency=EUR \
  --DevLedger:Accounts:0:Balance=50000
