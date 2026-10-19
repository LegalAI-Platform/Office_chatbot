#!/bin/bash
# TASK 02 tests that need no Meta account:
#   1. database rules with the fixtures in n8n/fixtures/whatsapp (as office_app)
#   2. the real n8n wiring through WA-TEST-HARNESS
source "$(dirname "$0")/_common.sh"
require_env_file
failed=0
echo "==== Database rules (fixtures)"
docker compose run --rm tools wa-db-tests || failed=1
echo
echo "==== n8n workflows (WA-TEST-HARNESS)"
if result="$(run_workflow WaTestHarness001)"; then
  docker compose exec -T n8n node /opt/office/n8n/scripts/check-harness.js <<<"$result" || failed=1
else
  echo "FAIL  WA-TEST-HARNESS did not run"; failed=1
fi
exit $failed
