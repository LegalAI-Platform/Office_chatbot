#!/bin/bash
# Stops every service (all profiles). Data volumes are kept.
source "$(dirname "$0")/_common.sh"
require_env_file
docker compose "${ALL_PROFILES[@]}" down --remove-orphans
