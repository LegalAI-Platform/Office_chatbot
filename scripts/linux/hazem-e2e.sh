#!/bin/bash
# End-to-end test of n8n <-> .NET API (DECISIONS v1.3) without Meta:
#   1. a text message -> n8n -> API (echo) -> n8n sends it (dry run) -> result back to the API
#   2. the same turn handed over again -> still exactly one reply
#   3. API stopped -> the message waits as pending; API started -> it is handled
#   4. API -> n8n internal send (CLI send-test), and both tokens refused when wrong
# Uses dummy numbers 20100000006x. Dry run and echo are switched off again at the end.
source "$(dirname "$0")/_common.sh"
check_no_placeholders

# The tools image is rebuilt on every run (pull_policy: build); hide that build log.
step() { docker compose run --rm --no-deps tools hazem-e2e "$@" 2> >(grep -vE '^#[0-9]+ |Image office-tools|^\s*$' >&2); }
forward() { run_workflow WaFwdTurn0000001 >/dev/null || { echo "FAIL  WA-FORWARD-TURN did not run"; failed=1; }; }
failed=0
trap 'docker compose up -d --wait api >/dev/null 2>&1; step cleanup' EXIT

echo "==== 1. Message -> API -> reply"
step setup
forward
step check-loop || failed=1

echo "==== 2. Same turn again"
step replay
forward
step check-replay || failed=1

echo "==== 3. API down, then back"
docker compose stop api >/dev/null 2>&1
step inject-b
forward
step check-api-down || failed=1
docker compose up -d --wait api >/dev/null 2>&1
forward
step check-recovered || failed=1

echo "==== 4. API -> n8n, and tokens"
docker compose exec -T api dotnet /app/cli/Office.Cli.dll send-test --to 201000000061 --text "e2e: API -> n8n" >/dev/null \
  || { echo "FAIL  CLI send-test did not succeed"; failed=1; }
step check-api-send || failed=1
step check-auth || failed=1

echo
[[ $failed -eq 0 ]] && echo "hazem-e2e: all steps passed." || echo "hazem-e2e: some steps FAILED."
exit $failed
