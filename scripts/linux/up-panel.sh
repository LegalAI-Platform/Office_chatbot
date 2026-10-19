#!/bin/bash
# Starts the core services plus Appsmith (profile "panel").
source "$(dirname "$0")/_common.sh"
check_no_placeholders
docker compose --profile panel up -d --wait
docker compose --profile panel ps
echo "Appsmith: http://127.0.0.1:8080   n8n: http://127.0.0.1:5678"
