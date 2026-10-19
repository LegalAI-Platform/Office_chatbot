#!/bin/bash
# Dumps the office database to s3://office-backups (kept 30 days).
# Daily schedule (cron, 03:00 Cairo time):
#   0 3 * * * /path/to/repo/scripts/linux/backup.sh >> /var/log/office-backup.log 2>&1
source "$(dirname "$0")/_common.sh"
require_env_file
docker compose run --rm tools backup
