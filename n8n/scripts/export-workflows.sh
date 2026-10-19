#!/bin/sh
# Runs INSIDE the n8n container: exports the project's workflows (the ids found in
# n8n/workflows/*.json) to /tmp/office-export with credential ids replaced by
# CRED:<name>. The host script copies the result back into n8n/workflows/.
set -eu
SRC=/opt/office/n8n/workflows
OUT=/tmp/office-export
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
rm -rf "$OUT" && mkdir -p "$OUT" "$TMP/raw"

if ! n8n export:credentials --all --output="$TMP/creds.json" >/dev/null 2>&1; then
  echo "[]" > "$TMP/creds.json"
fi
for f in "$SRC"/*.json; do
  id="$(node -e 'console.log(JSON.parse(require("fs").readFileSync(process.argv[1], "utf8")).id)' "$f")"
  n8n export:workflow --id="$id" --output="$TMP/raw/$id.json" >/dev/null
done
node /opt/office/n8n/scripts/swap-credentials.js to-names "$TMP/creds.json" "$TMP/raw" "$OUT"
