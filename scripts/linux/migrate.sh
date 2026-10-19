#!/bin/bash
# Runs dbmate migrations on the office database as office_migrator.
#   Usage: migrate.sh up|down|status
source "$(dirname "$0")/_common.sh"
require_env_file
cmd="${1:-status}"
case "$cmd" in up|down|status|rollback) ;; *) die "usage: migrate.sh up|down|status" ;; esac
[[ "$cmd" == "rollback" ]] && cmd=down
docker compose run --rm migrate "$cmd"
