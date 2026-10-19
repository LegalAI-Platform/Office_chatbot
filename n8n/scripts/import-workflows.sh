#!/bin/sh
# Runs INSIDE the n8n container: imports every workflow in n8n/workflows/,
# replacing CRED:<name> placeholders with this instance's credential ids.
# n8n 2.x only runs PUBLISHED sub-workflows and error workflows, so workflows whose
# only trigger is "Execute Workflow Trigger" or "Error Trigger" are published here.
# Workflows with external triggers (WhatsApp, schedule) stay unpublished:
# publish them with tunnel-refresh / by hand once their credentials exist.
set -eu
SRC=/opt/office/n8n/workflows
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# Only id/name/type are used; the exported data stays encrypted and is deleted on exit.
if ! n8n export:credentials --all --output="$TMP/creds.json" >/dev/null 2>&1; then
  echo "[]" > "$TMP/creds.json"
fi
node /opt/office/n8n/scripts/swap-credentials.js to-ids "$TMP/creds.json" "$SRC" "$TMP/wf"
n8n import:workflow --separate --input="$TMP/wf" 2>&1 | grep -vE '^\s*$|license SDK|Postgres 16' | tail -2

ids="$(node -e '
  const fs = require("fs"), dir = process.argv[1];
  const internal = new Set(["n8n-nodes-base.executeWorkflowTrigger", "n8n-nodes-base.errorTrigger"]);
  for (const f of fs.readdirSync(dir).filter((x) => x.endsWith(".json"))) {
    const wf = JSON.parse(fs.readFileSync(dir + "/" + f, "utf8"));
    const triggers = wf.nodes.filter((n) => /Trigger$|trigger$/.test(n.type) || n.type.endsWith(".manualTrigger"));
    if (triggers.length && triggers.every((n) => internal.has(n.type))) console.log(wf.id);
  }' "$SRC")"
for id in $ids; do
  n8n publish:workflow --id="$id" >/dev/null 2>&1 && echo "published $id" || echo "FAILED to publish $id"
done
