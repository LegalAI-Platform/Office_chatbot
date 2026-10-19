#!/bin/bash
# Applies EF Core migrations and the Hangfire schema as office_migrator (Office.Cli).
#   Usage: migrate.sh [up|status]
source "$(dirname "$0")/_common.sh"
require_env_file
case "${1:-up}" in
  up)     docker compose run --rm cli migrate ;;
  status) docker compose run --rm cli migrations-list ;;
  *)      die "usage: migrate.sh [up|status]   (roll back with: cli.sh migrate --to <MigrationName>)" ;;
esac
