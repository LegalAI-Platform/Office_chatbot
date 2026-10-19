#!/bin/bash
# Starts the core services: postgres, redis, garage, storage-init, api.
source "$(dirname "$0")/_common.sh"
check_no_placeholders
docker compose up -d --build --wait
docker compose ps
echo "API: http://127.0.0.1:8081/health/ready"
