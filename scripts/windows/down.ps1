# Stops every service (all profiles). Data volumes are kept.
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-EnvFile
Invoke-Compose @AllProfiles down --remove-orphans
