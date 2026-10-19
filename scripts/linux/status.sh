#!/bin/bash
# Shows every container, its health and its published ports.
source "$(dirname "$0")/_common.sh"
require_env_file
docker compose "${ALL_PROFILES[@]}" ps -a --format 'table {{.Service}}\t{{.Status}}\t{{.Ports}}'
