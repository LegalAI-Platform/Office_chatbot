#!/bin/bash
# Prints fresh random secrets. Copy them into .env yourself; this script never edits .env.
source "$(dirname "$0")/_common.sh"
docker build -q -t office-tools:local infra/tools >/dev/null
docker run --rm office-tools:local gen-secrets
