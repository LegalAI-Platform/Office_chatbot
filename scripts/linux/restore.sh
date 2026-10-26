#!/bin/bash
# Restores a backup and checks it (docs/runbooks/backup-restore.md). Never overwrites a live database or a file.
#   restore.sh list [office|n8n|chatwoot|files]
#   restore.sh <office|n8n|chatwoot> <latest|FILE_NAME> [TARGET_DB] [--drop-after-verify] [--structure-only]
#   restore.sh files <latest|MANIFEST_NAME> [--into-live]
source "$(dirname "$0")/_common.sh"
require_env_file
[[ $# -ge 1 ]] || die "usage: restore.sh list | restore.sh <office|n8n|chatwoot> <latest|FILE_NAME> [TARGET_DB] [--drop-after-verify] | restore.sh files <latest|MANIFEST> [--into-live]"
docker compose run --rm tools restore "$@"
