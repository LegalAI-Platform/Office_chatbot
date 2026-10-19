#!/bin/bash
# Follows the logs of one service.   Usage: logs.sh <service> [lines]
source "$(dirname "$0")/_common.sh"
require_env_file
[[ $# -ge 1 ]] || die "usage: logs.sh <postgres|redis|garage|n8n|appsmith|tunnel|tunnel-named> [lines]"
docker compose "${ALL_PROFILES[@]}" logs -f --tail "${2:-200}" "$1"
