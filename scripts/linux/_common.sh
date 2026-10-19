#!/bin/bash
# Shared helpers for scripts/linux/*.sh. Sourced, not executed.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

ALL_PROFILES=(--profile panel --profile tunnel --profile tunnel-named --profile tools)

# Git Bash on Windows rewrites container paths such as /opt/...; harmless on Linux.
export MSYS_NO_PATHCONV=1

# TASK 02: WhatsApp inbound workflow and its fixed webhook id (see n8n/workflows/META-WA-INBOUND.json).
INBOUND_WORKFLOW_ID=WaInbound0000001
INBOUND_WEBHOOK_ID=3c1f0b6e-6a52-4f0e-9a51-0e0c0a5ea001

# Runs a workflow with the n8n CLI inside the container; prints the last node's JSON.
run_workflow() { docker compose exec -T n8n sh /opt/office/n8n/scripts/run-workflow.sh "$1"; }

# Replaces (or appends) NAME=VALUE in .env.
set_env_value() {
  if grep -qE "^$1=" .env; then
    sed -i "s#^$1=.*#$1=$2#" .env
  else
    echo "$1=$2" >> .env
  fi
}

n8n_owner_ready() {
  curl -s --max-time 10 http://127.0.0.1:5678/rest/settings | grep -q '"showSetupOnFirstLoad":false'
}

tunnel_url_from_logs() {
  docker compose --profile tunnel logs tunnel 2>/dev/null | grep -oE 'https://[a-z0-9-]+\.trycloudflare\.com' | tail -1 || true
}

die() { echo "ERROR: $*" >&2; exit 1; }

require_env_file() {
  [[ -f .env ]] || die ".env not found. Run: cp .env.example .env  (then fill it, see README.md)"
}

env_value() {  # env_value NAME -> value from .env (empty if missing)
  local line
  line="$(grep -E "^$1=" .env | tail -1 || true)"
  echo "${line#*=}"
}

# Refuse to start anything while .env still has CHANGE_ME in a non-comment line.
check_no_placeholders() {
  require_env_file
  if grep -vE '^\s*#' .env | grep -q 'CHANGE_ME'; then
    echo "ERROR: .env still contains CHANGE_ME values:" >&2
    grep -nvE '^\s*#' .env | grep 'CHANGE_ME' | cut -d= -f1 >&2
    die "replace them (scripts/linux/gen-secrets.sh prints fresh values)"
  fi
}
