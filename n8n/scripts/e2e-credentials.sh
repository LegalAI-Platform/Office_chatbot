#!/bin/sh
# Runs INSIDE the n8n container, for hazem-e2e only (TASK 03).
#   e2e-credentials.sh add    creates meta-whatsapp-token and openai-transcription with FIXED test
#                             ids (TestMetaToken001, TestOpenAiKey001) and the value
#                             'stub-not-a-real-key' — ONLY for a name that does not exist yet.
#                             An existing credential (the owner's real one) is never read or changed.
#   e2e-credentials.sh list   prints the names that exist (no data).
# Removal is done by hazem-e2e on the n8n database, by fixed id AND name, then n8n-import runs
# again so the workflows go back to the CRED:<name> placeholders (the n8n 2.43 CLI cannot delete).
set -eu
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

names() {
  if n8n export:credentials --all --output="$TMP/all.json" >/dev/null 2>&1; then
    node -e 'for (const c of JSON.parse(require("fs").readFileSync(process.argv[1], "utf8"))) console.log(c.name)' "$TMP/all.json"
  fi
}

case "${1:-}" in
  list) names ;;
  add)
    existing="$(names)"
    for pair in "TestMetaToken001:meta-whatsapp-token" "TestOpenAiKey001:openai-transcription"; do
      id="${pair%%:*}"; name="${pair#*:}"
      if echo "$existing" | grep -qx "$name"; then
        echo "kept $name (already exists, not touched)"
        continue
      fi
      cat > "$TMP/c.json" <<JSON
[{ "id": "$id", "name": "$name", "type": "httpHeaderAuth",
   "data": { "name": "Authorization", "value": "Bearer stub-not-a-real-key" } }]
JSON
      n8n import:credentials --input="$TMP/c.json" >/dev/null
      echo "created $name ($id)"
    done
    ;;
  *) echo "usage: e2e-credentials.sh add|list" >&2; exit 2 ;;
esac
