[CmdletBinding()]
param([string]$Container = 'inovaged-ai-operational-pg', [int]$Port = 55439)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '../..')
$database = 'ai_install_' + [guid]::NewGuid().ToString('N')
$output = Join-Path $root 'artifacts/ai-operational'
New-Item -ItemType Directory -Force $output | Out-Null
Set-Content -LiteralPath (Join-Path $output 'install-database.txt') -Value $database
docker exec $Container psql -X -q -v ON_ERROR_STOP=1 -U postgres -d postgres -c "create database $database;"
if ($LASTEXITCODE -ne 0) { throw 'Could not create disposable database.' }
$previous = $env:ConnectionStrings__DefaultConnection
try {
    $env:ConnectionStrings__DefaultConnection = "Host=127.0.0.1;Port=$Port;Database=$database;Username=postgres;Password=local_disposable_only"
    dotnet run --project (Join-Path $root 'InovaGed.Database.Migrator') -- install --verify 2>&1 | Tee-Object -FilePath (Join-Path $output 'install-latest.log')
    if ($LASTEXITCODE -ne 0) { exit 1 }
    dotnet run --no-build --project (Join-Path $root 'InovaGed.Database.Migrator') -- install --verify 2>&1 | Tee-Object -FilePath (Join-Path $output 'install-replay.log')
    exit $LASTEXITCODE
} finally { $env:ConnectionStrings__DefaultConnection = $previous }
