# Runs the panel's tests (web/: type-check, Vitest, build with the bundle budget) in a Node
# container, so the machine needs no Node. The sources are copied inside the container, so the
# host's node_modules is never used or changed.
. (Join-Path $PSScriptRoot '_common.ps1')
$nodeImage = 'node:24.21.0-alpine3.23'
& docker run --rm -v "$(Join-Path $Root 'web'):/src:ro" -v office_npm:/root/.npm $nodeImage sh -c `
    'mkdir /work && cd /src && find . -mindepth 1 -maxdepth 1 ! -name node_modules ! -name dist -exec cp -r {} /work/ \; && cd /work && npm ci --ignore-scripts --no-audit --no-fund && npm test && npm run build'
exit $LASTEXITCODE
