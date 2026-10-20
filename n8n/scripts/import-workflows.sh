#!/bin/sh
# Runs INSIDE the n8n container: imports every workflow in n8n/workflows/,
# replacing CRED:<name> placeholders with this instance's credential ids.
# n8n 2.x only runs PUBLISHED sub-workflows, error workflows, schedules and webhooks, so a
# workflow is published here when:
#   - its triggers are only Execute Workflow / Error / Schedule / Webhook / Manual
#     (not only Manual: those are run by hand), and
#   - for workflows that run by themselves (schedule, webhook): every credential their
#     enabled nodes use exists in this n8n. Sub-workflows are always published.
# META-WA-INBOUND (WhatsApp Trigger) is never published here: tunnel-refresh does it.
# The running n8n only starts new schedules and webhooks after a restart; the last line
# printed is RESTART_N8N when that is needed (n8n-import does the restart).
set -eu
SRC=/opt/office/n8n/workflows
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# Workflows that were removed from the repository: unpublish them if this n8n still has them.
RETIRED="WaEchoTest000001"

# Only id/name/type are used; the exported data stays encrypted and is deleted on exit.
if ! n8n export:credentials --all --output="$TMP/creds.json" >/dev/null 2>&1; then
  echo "[]" > "$TMP/creds.json"
fi
node /opt/office/n8n/scripts/swap-credentials.js to-ids "$TMP/creds.json" "$SRC" "$TMP/wf"
n8n import:workflow --separate --input="$TMP/wf" 2>&1 | grep -vE '^\s*$|license SDK|Postgres 16' | tail -2

for id in $RETIRED; do
  n8n unpublish:workflow --id="$id" >/dev/null 2>&1 && echo "unpublished retired $id" || true
done

plan="$(node -e '
  const fs = require("fs"), dir = process.argv[1];
  const allowed = new Set(["n8n-nodes-base.executeWorkflowTrigger", "n8n-nodes-base.errorTrigger",
    "n8n-nodes-base.scheduleTrigger", "n8n-nodes-base.webhook", "n8n-nodes-base.manualTrigger"]);
  const needsRestart = new Set(["n8n-nodes-base.scheduleTrigger", "n8n-nodes-base.webhook"]);
  for (const f of fs.readdirSync(dir).filter((x) => x.endsWith(".json"))) {
    const wf = JSON.parse(fs.readFileSync(dir + "/" + f, "utf8"));
    const triggers = wf.nodes.filter((n) => /trigger$/i.test(n.type) || n.type === "n8n-nodes-base.webhook");
    if (!triggers.length || !triggers.every((n) => allowed.has(n.type))) continue;
    if (triggers.every((n) => n.type === "n8n-nodes-base.manualTrigger")) continue;
    const runsByItself = triggers.some((n) => needsRestart.has(n.type));
    const missing = wf.nodes.some((n) => !n.disabled && Object.values(n.credentials || {}).some((c) => String(c.id).startsWith("CRED:")));
    if (runsByItself && missing) { console.log(`skip ${wf.id} ${wf.name}`); continue; }
    console.log(`publish ${wf.id} ${runsByItself ? "restart" : "-"}`);
  }' "$TMP/wf")"

echo "$plan" | while read -r action id extra; do
  [ -n "${action:-}" ] || continue
  if [ "$action" = skip ]; then
    echo "not published $id ($extra): some of its credentials are missing"
  elif n8n publish:workflow --id="$id" >/dev/null 2>&1; then
    echo "published $id"
  else
    echo "FAILED to publish $id"
  fi
done
echo "$plan" | grep -q '^publish .* restart$' && echo RESTART_N8N || true
