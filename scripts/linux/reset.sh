#!/bin/bash
# DESTRUCTIVE: stops everything and deletes ALL data volumes (database, files).
# Only allowed when ENVIRONMENT=test, and only after typing RESET.
source "$(dirname "$0")/_common.sh"
require_env_file

env_name="$(env_value ENVIRONMENT)"
[[ "$env_name" == "test" ]] || die "reset is only allowed when ENVIRONMENT=test (current: '${env_name}')"

echo "This deletes every volume: PostgreSQL (office), Redis and object storage data."
read -r -p "Type RESET to continue: " answer
[[ "$answer" == "RESET" ]] || die "aborted (you typed '$answer')"

docker compose "${ALL_PROFILES[@]}" down --volumes --remove-orphans
echo "All volumes deleted. Start again with scripts/linux/up.sh (migrations run automatically)."
