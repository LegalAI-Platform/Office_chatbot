#!/bin/bash
# Runs the panel's tests (web/: type-check, Vitest, build with the bundle budget) in a Node
# container, so the machine needs no Node. The sources are copied inside the container, so the
# host's node_modules is never used or changed.
source "$(dirname "$0")/_common.sh"
NODE_IMAGE=node:24.21.0-alpine3.23
MSYS_NO_PATHCONV=1 docker run --rm -v "$ROOT/web:/src:ro" -v office_npm:/root/.npm "$NODE_IMAGE" sh -c \
  'mkdir /work && cd /src && find . -mindepth 1 -maxdepth 1 ! -name node_modules ! -name dist -exec cp -r {} /work/ \; && cd /work && npm ci --ignore-scripts --no-audit --no-fund && npm test && npm run build'
