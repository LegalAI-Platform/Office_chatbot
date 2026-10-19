# Exports the project's workflows from n8n back into n8n/workflows/ (credential ids
# replaced by CRED:<name>, volatile fields removed). Review the git diff afterwards.
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-EnvFile
Invoke-Compose exec -T n8n sh /opt/office/n8n/scripts/export-workflows.sh
Invoke-Compose cp n8n:/tmp/office-export/. n8n/workflows/ | Out-Null
Write-Host "Exported to n8n/workflows/. Check: git diff n8n/workflows"
