[CmdletBinding()]
param([int]$Port = 5189)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$database = (Get-Content (Join-Path $root 'artifacts/ai-operational/install-database.txt')).Trim()
if ($database -notmatch '^ai_install_[a-f0-9]{32}$') { throw 'Only the disposable installation database is allowed.' }
docker exec inovaged-ai-operational-pg psql -X -q -v ON_ERROR_STOP=1 -U postgres -d $database -c "insert into ged.tenant(id,name,code) values ('00000000-0000-0000-0000-000000000001','Instituicao Ficticia Local','default') on conflict(id) do nothing;"
if ($LASTEXITCODE -ne 0) { throw 'Could not prepare the fictional tenant.' }
$env:DOTNET_ENVIRONMENT = 'Homologation'
$env:ASPNETCORE_ENVIRONMENT = 'Homologation'
$env:AllowedHosts = '127.0.0.1;localhost'
$env:ConnectionStrings__DefaultConnection = "Host=127.0.0.1;Port=55439;Database=$database;Username=postgres;Password=local_disposable_only"
$env:Storage__Local__RootPath = Join-Path $root 'artifacts/ai-operational/storage'
$env:PacsIntegration__Enabled = 'false'
$env:SystemSeed__Enabled = 'true'
$env:SystemSeed__AllowInPoc = 'true'
$env:SystemSeed__FailFastOnSeedError = 'true'
$env:INOVAGED_DEV_SEED_PASSWORD = 'Ficticio-local-2026!'
$env:INOVAGED_AI_DETERMINISTIC = '1'
$env:DocumentAi__Enabled = 'true'
$env:DocumentAi__Provider = 'Deterministic'
$env:DocumentAi__Providers__Deterministic__Enabled = 'true'
$env:DocumentAi__Providers__Deterministic__AllowedModels__0 = 'deterministic-v1'
foreach ($task in @('Summarize','ExtractMetadata','SuggestClassification','SuggestArchivalClassification','SupportProtocol')) {
    [Environment]::SetEnvironmentVariable('DocumentAi__TaskModels__' + $task, 'deterministic-v1')
}
$env:Database__FailFastOnInvalidSchema = 'true'
dotnet run --no-build --no-launch-profile --project (Join-Path $root 'InovaGed.Web') --urls "http://127.0.0.1:$Port" 2>&1 | Tee-Object -FilePath (Join-Path $root 'artifacts/ai-operational/web-local.log')
exit $LASTEXITCODE
