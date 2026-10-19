# Runs the backend unit and integration tests.
# Integration tests create a throwaway database (office_test_*) on the local PostgreSQL,
# migrate it as office_migrator, run the API as office_app, and drop it afterwards.
#   backend-test.ps1            local .NET SDK if installed, otherwise the SDK container
#   backend-test.ps1 -Docker    always use the SDK container
param([switch]$Docker)
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-EnvFile

$sdkImage = 'mcr.microsoft.com/dotnet/sdk:10.0.401-alpine3.23'
$vars = 'POSTGRES_SUPERUSER', 'POSTGRES_SUPERUSER_PASSWORD', 'OFFICE_MIGRATOR_PASSWORD', 'OFFICE_APP_PASSWORD', 'REDIS_PASSWORD'
foreach ($v in $vars) { Set-Item -Path "env:$v" -Value (Get-EnvValue $v) }

if (-not $Docker -and (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $env:OFFICE_TEST_PG_HOST = '127.0.0.1'
    $env:OFFICE_TEST_REDIS_HOST = '127.0.0.1'
    Push-Location (Join-Path $Root 'backend')
    try { & dotnet test --solution Office.sln; $code = $LASTEXITCODE } finally { Pop-Location }
    exit $code
}

$envArgs = @()
foreach ($v in $vars) { $envArgs += @('-e', $v) }
# Copy the sources inside the container so Windows bin/obj folders are never reused.
& docker run --rm --network office_net @envArgs `
    -e OFFICE_TEST_PG_HOST=postgres -e OFFICE_TEST_REDIS_HOST=redis `
    -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 `
    -v "$(Join-Path $Root 'backend'):/src:ro" -v office_nuget:/root/.nuget/packages `
    $sdkImage sh -c 'cp -r /src /work && cd /work && find . -name bin -o -name obj -o -name .nuget-packages | xargs rm -rf && rm -f NuGet.config && dotnet test --solution Office.sln'
exit $LASTEXITCODE
