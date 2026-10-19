#!/bin/bash
# Runs the backend unit and integration tests.
# Integration tests create a throwaway database (office_test_*) on the local PostgreSQL,
# migrate it as office_migrator, run the API as office_app, and drop it afterwards.
#   backend-test.sh            local .NET SDK if installed, otherwise the SDK container
#   backend-test.sh --docker   always use the SDK container
source "$(dirname "$0")/_common.sh"
require_env_file

SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.401-alpine3.23
vars=(POSTGRES_SUPERUSER POSTGRES_SUPERUSER_PASSWORD OFFICE_MIGRATOR_PASSWORD OFFICE_APP_PASSWORD REDIS_PASSWORD)
for v in "${vars[@]}"; do export "$v=$(env_value "$v")"; done

if [[ "${1:-}" != "--docker" ]] && command -v dotnet >/dev/null 2>&1; then
  export OFFICE_TEST_PG_HOST=127.0.0.1 OFFICE_TEST_REDIS_HOST=127.0.0.1
  cd backend
  dotnet test --solution Office.sln
else
  env_args=(); for v in "${vars[@]}"; do env_args+=(-e "$v"); done
  # Copy the sources inside the container so Windows bin/obj folders are never reused.
  docker run --rm --network office_net "${env_args[@]}" \
    -e OFFICE_TEST_PG_HOST=postgres -e OFFICE_TEST_REDIS_HOST=redis \
    -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 \
    -v "$ROOT/backend:/src:ro" -v office_nuget:/root/.nuget/packages \
    "$SDK_IMAGE" sh -c 'cp -r /src /work && cd /work && find . -name bin -o -name obj -o -name .nuget-packages | xargs rm -rf && rm -f NuGet.config && dotnet test --solution Office.sln'
fi
