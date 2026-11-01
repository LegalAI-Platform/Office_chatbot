# TASK 18 §3.1: plays docs\ai\conversations.jsonl through the real Hazem (in-process API, throwaway database, fake calendar,
# the real classifier and writer) and has the judge (Claude Opus, docs\ai\judge-v1.md) score every reply. It calls the real
# AI and costs money: it needs HAZEM_ANTHROPIC_API_KEY in .env (entered by the owner; never printed here).
# The report: docs\ai\quality\<label>.md and .json.
#   quality-eval.ps1 [-Label baseline] [-Only id1,id2]
param([string]$Label = 'baseline', [string]$Only = '')
. (Join-Path $PSScriptRoot '_common.ps1')
Assert-EnvFile

$vars = 'POSTGRES_SUPERUSER', 'POSTGRES_SUPERUSER_PASSWORD', 'OFFICE_MIGRATOR_PASSWORD', 'OFFICE_APP_PASSWORD', 'OFFICE_API_PASSWORD', 'HAZEM_ANTHROPIC_API_KEY'
foreach ($v in $vars) { Set-Item -Path "env:$v" -Value (Get-EnvValue $v) }
if (-not $env:HAZEM_ANTHROPIC_API_KEY) { Fail 'HAZEM_ANTHROPIC_API_KEY is empty in .env (the owner enters it).' }
$env:QUALITY_EVAL = '1'; $env:QUALITY_LABEL = $Label; $env:QUALITY_ONLY = $Only

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Fail 'quality-eval needs the .NET SDK on this machine (or run scripts/linux/quality-eval.sh).' }
$env:OFFICE_TEST_PG_HOST = '127.0.0.1'
$env:OFFICE_TEST_MIGRATIONS_DIR = Join-Path $Root 'database\migrations'
$env:OFFICE_TEST_REPO_DIR = $Root
Push-Location (Join-Path $Root 'backend')
try { & dotnet test --project tests/Office.IntegrationTests --filter-class Office.IntegrationTests.QualityEvalTests; $code = $LASTEXITCODE } finally { Pop-Location }
exit $code
