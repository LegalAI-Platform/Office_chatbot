#!/bin/bash
# Runs an Office.Cli command in a container.
#   cli.sh migrate | migrations-list | wa-subscribe --callback URL | wa-subscribe-waba
#   cli.sh wa-check | templates-submit | templates-sync | send-test --to WA_ID [--text TEXT]
source "$(dirname "$0")/_common.sh"
require_env_file
[[ $# -ge 1 ]] || die "usage: cli.sh <command> [options]"
docker compose run --rm cli "$@"
