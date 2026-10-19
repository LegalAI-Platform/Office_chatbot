#!/bin/bash
# Restores a backup into a NEW database and verifies row counts. Never overwrites office or n8n.
#   restore.sh list [office|n8n]
#   restore.sh <office|n8n> <latest|FILE_NAME> [TARGET_DB] [--drop-after-verify]
source "$(dirname "$0")/_common.sh"
require_env_file
[[ $# -ge 1 ]] || die "usage: restore.sh list | restore.sh <office|n8n> <latest|FILE_NAME> [TARGET_DB] [--drop-after-verify]"
docker compose run --rm tools restore "$@"
