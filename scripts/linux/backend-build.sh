#!/bin/bash
# Builds the backend image (API + CLI). Needs Docker only, no local .NET SDK.
source "$(dirname "$0")/_common.sh"
require_env_file
docker compose build api
