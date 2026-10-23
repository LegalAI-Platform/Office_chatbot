#!/bin/bash
# End-to-end test of n8n <-> .NET API without Meta or OpenAI (DECISIONS v1.3 + TASK 03):
#   1. a text message -> n8n -> API (echo) -> n8n sends it (dry run) -> result back to the API
#   2. the same turn handed over again -> still exactly one reply
#   3. API stopped -> the turn waits as pending; API started -> handled with the same turn_id
#   4. API -> n8n internal send (CLI send-test), and both tokens refused when wrong
#   5. two texts and a photo within 8 s -> one turn, one reply; the photo in office-sensitive
#   6. PDF, voice note (transcribed by the stub), too large, wrong sha256, forbidden type
#   7. a turn left processing while n8n restarts -> taken again, one reply
#   8. TASK 04: Hazem answers from approved texts (welcome, name, offer, B-20), a photo never
#      reaches the AI, a phone number is masked, and the AI stub down still gets an answer
#   9. TASK 05a: a listed client gets the neutral reply and a task for their staff, a blocked number
#      gets nothing (muted), a topic told first means no topic question after the name
#  10. TASK 07a: a staff account (created with the CLI, removed at the end) signs in to the panel
#      through the web container, finds that task under «مهامي», sees the client's words, starts
#      and finishes it; the panel never reaches n8n's endpoints
# Meta and OpenAI are the meta-stub container (profile tests). If meta-whatsapp-token or
# openai-transcription do not exist, temporary ones (fixed test ids) are created and removed
# again; existing (real) credentials are never touched. Everything is restored at the end.
source "$(dirname "$0")/_common.sh"
check_no_placeholders

# The tools image is rebuilt on every run (pull_policy: build); hide that build log.
step() { docker compose run --rm --no-deps tools hazem-e2e "$@" 2> >(grep -vE '^#[0-9]+ |Image office-tools|^\s*$' >&2); }
forward() { run_workflow WaFwdTurn0000001 >/dev/null || { echo "FAIL  WA-FORWARD-TURN did not run"; failed=1; }; }
import_warning() {  # runs n8n-import; prints its "credentials not found" line (or "none")
  local out
  out="$(bash "$ROOT/scripts/linux/n8n-import.sh" 2>&1)" || { echo "n8n-import failed: $out" >&2; return 1; }
  grep -E '^WARNING: credentials not found' <<<"$out" || echo "none"
}
failed=0

finish_up() {
  docker compose up -d --wait api >/dev/null 2>&1
  step cleanup
  docker compose --profile tests rm -sf meta-stub >/dev/null 2>&1
  step remove-test-credentials
  echo "==== Restore n8n (re-import without the test credentials)"
  after="$(import_warning)"
  if [[ "$after" == "$before" ]]; then
    echo "PASS  n8n credentials are exactly as before the test"
  else
    echo "FAIL  n8n credentials changed (before: $before / after: $after)"; failed=1
  fi
  step check-restored || failed=1
  echo
  [[ $failed -eq 0 ]] && echo "hazem-e2e: all steps passed." || echo "hazem-e2e: some steps FAILED."
  exit $failed
}

echo "==== 0. Preparation: stub, temporary credentials"
before="$(import_warning)" || exit 1
echo "      before: $before"
trap finish_up EXIT
docker compose --profile tests up -d --wait meta-stub >/dev/null 2>&1 || { echo "FAIL  meta-stub did not start"; failed=1; exit 1; }
docker compose exec -T n8n sh /opt/office/n8n/scripts/e2e-credentials.sh add
import_warning >/dev/null

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

echo "==== 5. Two texts and a photo within 8 seconds"
step group-c
sleep 10   # the 8-second quiet period
forward
step check-group || failed=1

echo "==== 6. PDF, voice note, too large, wrong sha256, forbidden type"
step media-d
sleep 10
forward
step check-media || failed=1

echo "==== 7. Turn stuck while n8n restarts"
step stale-e
docker compose restart n8n >/dev/null 2>&1
docker compose up -d --wait n8n >/dev/null 2>&1
sleep 22   # whatsapp.turn_stale_seconds is 20 during this step
forward
step check-stale || failed=1

echo "==== 8. TASK 04: Hazem understands (AI stub) and answers from approved texts"
step hazem-setup
forward
step check-hazem-welcome || failed=1
step hazem-name
forward
step check-hazem-offer || failed=1
step hazem-legal
forward
step check-hazem-legal || failed=1
step hazem-ai-down
forward
step check-hazem-fallback || failed=1

echo "==== 9. TASK 05a: lists (B-14, B-04), the topic (B-25), the source (B-13)"
step hazem05-setup
forward
step check-hazem05 || failed=1
step hazem05-name
forward
step check-hazem05-name || failed=1

echo "==== 10. TASK 07a: the panel (a staff account signs in through the web container)"
# A throwaway password for the throwaway account: on stdin for the CLI, by name for the check.
export E2E_PANEL_PASSWORD="$(head -c 12 /dev/urandom | od -An -tx1 | tr -d ' 
')"
printf '%s
' "$E2E_PANEL_PASSWORD" | docker compose exec -T api dotnet /app/cli/Office.Cli.dll create-user   --name e2e-panel --display E2E --email e2e-panel@example.test --role staff --staff e2e-staff >/dev/null 2>&1   || { echo "FAIL  create-user e2e-panel (CLI)"; failed=1; }
docker compose run --rm --no-deps -e E2E_PANEL_PASSWORD tools hazem-e2e check-panel   2> >(grep -vE '^#[0-9]+ |Image office-tools|^\s*$' >&2) || failed=1
unset E2E_PANEL_PASSWORD
