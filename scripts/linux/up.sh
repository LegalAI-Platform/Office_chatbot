#!/bin/bash
# Starts the core services: postgres, redis, garage, storage-init, n8n.
source "$(dirname "$0")/_common.sh"
check_no_placeholders
docker compose up -d --wait
docker compose ps
echo "n8n editor: http://127.0.0.1:5678"
