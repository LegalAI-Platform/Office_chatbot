#!/bin/bash
# Shared helpers for scripts/linux/*.sh. Sourced, not executed.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

ALL_PROFILES=(--profile panel --profile tunnel --profile tunnel-named --profile tools)

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
