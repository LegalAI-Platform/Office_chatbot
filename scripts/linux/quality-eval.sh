#!/bin/bash
# TASK 18 §3.1: plays docs/ai/conversations.jsonl through the real Hazem (in-process API, throwaway database, fake calendar,
# the real classifier and writer) and has the judge (Claude Opus, docs/ai/judge-v1.md) score every reply. It calls the real
# AI and costs money: it needs HAZEM_ANTHROPIC_API_KEY in .env (entered by the owner; never printed here).
# The report: docs/ai/quality/<label>.md and .json.
#   quality-eval.sh [label] [id1,id2,...]     label defaults to "baseline"; the ids run some conversations only
source "$(dirname "$0")/_common.sh"
require_env_file

vars=(POSTGRES_SUPERUSER POSTGRES_SUPERUSER_PASSWORD OFFICE_MIGRATOR_PASSWORD OFFICE_APP_PASSWORD OFFICE_API_PASSWORD HAZEM_ANTHROPIC_API_KEY)
for v in "${vars[@]}"; do export "$v=$(env_value "$v")"; done
[[ -n "$HAZEM_ANTHROPIC_API_KEY" ]] || die "HAZEM_ANTHROPIC_API_KEY is empty in .env (the owner enters it)."
export QUALITY_EVAL=1 QUALITY_LABEL="${1:-baseline}" QUALITY_ONLY="${2:-}"

SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.403-alpine3.24
filter=(--filter-class Office.IntegrationTests.QualityEvalTests)
if command -v dotnet >/dev/null 2>&1; then
  export OFFICE_TEST_PG_HOST=127.0.0.1 # the tests find database/migrations and docs/ai by walking up
  cd "$ROOT/backend"
  dotnet test --project tests/Office.IntegrationTests "${filter[@]}"
else
  env_args=(); for v in "${vars[@]}" QUALITY_EVAL QUALITY_LABEL QUALITY_ONLY; do env_args+=(-e "$v"); done
  docker run --rm --network office_net "${env_args[@]}" \
    -e OFFICE_TEST_PG_HOST=postgres -e OFFICE_TEST_MIGRATIONS_DIR=/migrations -e OFFICE_TEST_REPO_DIR=/repo \
    -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 \
    -v "$ROOT/backend:/src:ro" -v "$ROOT/database/migrations:/migrations:ro" -v office_nuget:/root/.nuget/packages \
    -v "$ROOT/docs/ai:/repo/docs/ai" \
    "$SDK_IMAGE" sh -c 'mkdir /work && find /src -mindepth 1 -maxdepth 1 ! -name .vs ! -name .nuget-packages -exec cp -r {} /work/ \; && cd /work && find . -name bin -o -name obj | xargs rm -rf && rm -f NuGet.config && dotnet test --project tests/Office.IntegrationTests --filter-class Office.IntegrationTests.QualityEvalTests'
fi
