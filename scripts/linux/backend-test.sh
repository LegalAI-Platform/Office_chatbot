#!/bin/bash
# Runs the .NET backend unit and integration tests (DECISIONS v1.3).
# Integration tests create a throwaway database (office_test_*) on the local PostgreSQL,
# apply database/migrations as office_migrator (like dbmate), run the API as office_api,
# fake n8n at the HTTP boundary, and drop the database afterwards.
#   backend-test.sh            local .NET SDK if installed, otherwise the SDK container
#   backend-test.sh --docker   always use the SDK container
source "$(dirname "$0")/_common.sh"
require_env_file

SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.401-alpine3.23
vars=(POSTGRES_SUPERUSER POSTGRES_SUPERUSER_PASSWORD OFFICE_MIGRATOR_PASSWORD OFFICE_APP_PASSWORD OFFICE_API_PASSWORD)
for v in "${vars[@]}"; do export "$v=$(env_value "$v")"; done

if [[ "${1:-}" != "--docker" ]] && command -v dotnet >/dev/null 2>&1; then
  # The tests find database/migrations by walking up from their output folder.
  export OFFICE_TEST_PG_HOST=127.0.0.1
  cd backend
  dotnet test --solution Office.sln
else
  env_args=(); for v in "${vars[@]}"; do env_args+=(-e "$v"); done
  # Copy the sources inside the container so host bin/obj folders are never reused.
  docker run --rm --network office_net "${env_args[@]}" \
    -e OFFICE_TEST_PG_HOST=postgres -e OFFICE_TEST_MIGRATIONS_DIR=/migrations \
    -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 \
    -v "$ROOT/backend:/src:ro" -v "$ROOT/database/migrations:/migrations:ro" -v office_nuget:/root/.nuget/packages \
    "$SDK_IMAGE" sh -c 'cp -r /src /work && cd /work && find . -name bin -o -name obj -o -name .nuget-packages | xargs rm -rf && rm -f NuGet.config && dotnet test --solution Office.sln'
fi
