#!/bin/bash
# The backup now (TASK 13): the three databases and the client files, encrypted, to s3://office-backups (30 days).
# Every night it runs from nightly.sh (README, "Nightly job").
source "$(dirname "$0")/_common.sh"
require_env_file
docker compose run --rm tools backup
