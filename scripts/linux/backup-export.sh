#!/bin/bash
# A copy of the backups off this disk (TASK 13): the latest dumps and the client files, still encrypted, to a folder.
#   backup-export.sh /media/usb/OfficeBackups
source "$(dirname "$0")/_common.sh"
require_env_file
[[ $# -eq 1 && -d "$1" ]] || { echo "usage: backup-export.sh <existing folder>" >&2; exit 1; }
docker compose run --rm -v "$(cd "$1" && pwd):/export" tools backup-export
