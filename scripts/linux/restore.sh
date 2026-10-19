#!/bin/bash
# Restores a backup into a NEW database and verifies row counts. Never overwrites the live office database.
#   restore.sh list [office]
#   restore.sh office <latest|FILE_NAME> [TARGET_DB] [--drop-after-verify]
source "$(dirname "$0")/_common.sh"
require_env_file
[[ $# -ge 1 ]] || die "usage: restore.sh list | restore.sh office <latest|FILE_NAME> [TARGET_DB] [--drop-after-verify]"
docker compose run --rm tools restore "$@"
