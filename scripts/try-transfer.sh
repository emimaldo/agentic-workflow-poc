#!/usr/bin/env bash
set -euo pipefail

# Development helper: drive the transfer + HITL flow against a locally running API.
#
#   scripts/try-transfer.sh invoke "Transfer 12500 EUR to DE89 3704 0044 0532 0130 00"
#   scripts/try-transfer.sh get    <sessionId>
#   scripts/try-transfer.sh resume <sessionId> [true|false]
#
# BASE_URL defaults to the "fake" launch profile (http://localhost:5002); use
# BASE_URL=http://localhost:5270 for the "http" profile (SQL Server + Ollama).
# API_KEY defaults to the first key for TENANT_ID in appsettings.Development.json (gitignored).

BASE_URL=${BASE_URL:-http://localhost:5002}
TENANT_ID=${TENANT_ID:-tenant-local}
DEV_SETTINGS="$(dirname "$0")/../src/AgenticWorkflowPoC.Api/appsettings.Development.json"

if [[ -z "${API_KEY:-}" ]]; then
  API_KEY=$(python3 -I - "$DEV_SETTINGS" "$TENANT_ID" <<'PY'
import json, sys
with open(sys.argv[1], encoding="utf-8-sig") as f:
    keys = json.load(f).get("Authentication", {}).get("ApiKeys", [])
print(next((k["Key"] for k in keys if k.get("TenantId") == sys.argv[2]), ""))
PY
)
fi

if [[ -z "$API_KEY" ]]; then
  echo "No API key: set API_KEY or add Authentication:ApiKeys to appsettings.Development.json." >&2
  exit 1
fi

sessions="$BASE_URL/api/v1/tenants/$TENANT_ID/sessions"

case "${1:-}" in
  invoke)
    prompt=${2:?"usage: $0 invoke \"<prompt>\" [sessionId]"}
    session_id=${3:-$(python3 -I -c 'import uuid; print(uuid.uuid4())')}
    body=$(python3 -I -c 'import json, sys; print(json.dumps({"sessionId": sys.argv[1], "prompt": sys.argv[2]}))' "$session_id" "$prompt")
    curl -sS -i -X POST "$BASE_URL/api/agent/invoke" \
      -H "X-Api-Key: $API_KEY" -H "Content-Type: application/json" -d "$body"
    echo
    ;;
  get)
    curl -sS "$sessions/${2:?"usage: $0 get <sessionId>"}" -H "X-Api-Key: $API_KEY"
    echo
    ;;
  resume)
    session_id=${2:?"usage: $0 resume <sessionId> [true|false]"}
    approved=${3:-true}
    curl -sS -i -X POST "$sessions/$session_id/resume" \
      -H "X-Api-Key: $API_KEY" -H "Content-Type: application/json" -d "{\"isApproved\":$approved}"
    echo
    ;;
  *)
    sed -n '4,13p' "$0"
    exit 2
    ;;
esac
