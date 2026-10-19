# Builds the backend image (API + CLI). Needs Docker only, no local .NET SDK.
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-EnvFile
Invoke-Compose build api
