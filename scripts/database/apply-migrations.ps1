$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '../..')
dotnet run --project (Join-Path $root 'InovaGed.Database.Migrator') -- apply --verify
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
