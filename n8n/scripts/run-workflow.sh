#!/bin/sh
# Runs INSIDE the n8n container: executes a workflow with the n8n CLI and prints the
# JSON of the first item produced by the last node. Exit code 1 if the run failed.
#   sh /opt/office/n8n/scripts/run-workflow.sh <workflow_id>
# The CLI starts its own task broker, so it needs a port other than the running n8n's 5679.
set -eu
[ $# -eq 1 ] || { echo "usage: run-workflow.sh <workflow_id>" >&2; exit 2; }
OUT="$(mktemp)"
trap 'rm -f "$OUT"' EXIT
N8N_RUNNERS_BROKER_PORT="${N8N_CLI_BROKER_PORT:-5699}" n8n execute --id="$1" --rawOutput >"$OUT" 2>/dev/null || true
node -e '
  const txt = require("fs").readFileSync(process.argv[1], "utf8");
  const start = txt.indexOf("{"), end = txt.lastIndexOf("}");
  let run;
  try { run = JSON.parse(txt.slice(start, end + 1)); }
  catch { console.error("Could not read the execution result:\n" + txt.slice(-1500)); process.exit(1); }
  const rd = (run.data && run.data.resultData) || {};
  if (run.status !== "success" || rd.error) {
    const e = rd.error || {};
    console.error(`Workflow failed at node "${(e.node && e.node.name) || rd.lastNodeExecuted}": ${e.message || run.status}`);
    process.exit(1);
  }
  const last = rd.runData[rd.lastNodeExecuted];
  const item = last && last[0].data && last[0].data.main[0] && last[0].data.main[0][0];
  console.log(JSON.stringify(item ? item.json : {}, null, 2));
' "$OUT"
