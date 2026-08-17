[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ConnectionString,
    [string]$DataRoot = 'C:\InovaGed',
    [switch]$SkipMigrations,
    [switch]$RequireOcr
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
$webProject = Join-Path $repoRoot 'InovaGed.Web/InovaGed.Web.csproj'

& (Join-Path $PSScriptRoot 'Test-InovaGedPrerequisites.ps1') -RequireOcr:$RequireOcr -RequirePostgresTools
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$paths = @(
    $DataRoot,
    (Join-Path $DataRoot 'storage'),
    (Join-Path $DataRoot 'uploads'),
    (Join-Path $DataRoot 'logs'),
    (Join-Path $DataRoot 'backups')
)
foreach ($path in $paths) {
    New-Item -ItemType Directory -Force -Path $path | Out-Null
    $probe = Join-Path $path ".write-$([guid]::NewGuid().ToString('N'))"
    Set-Content -LiteralPath $probe -Value '' -NoNewline
    Remove-Item -LiteralPath $probe -Force
    Write-Host "[OK] Diretorio gravavel: $path" -ForegroundColor Green
}

# Enviar a connection string por stdin evita grava-la no repositorio e na linha de comando do processo.
$ConnectionString | dotnet user-secrets set 'ConnectionStrings:DefaultConnection' --project $webProject | Out-Null
dotnet user-secrets set 'Storage:Local:RootPath' (Join-Path $DataRoot 'storage') --project $webProject | Out-Null
dotnet user-secrets set 'Backup:RootPath' (Join-Path $DataRoot 'backups') --project $webProject | Out-Null

if (-not $SkipMigrations) {
    dotnet run --project (Join-Path $repoRoot 'InovaGed.Database.Migrator') -- apply --verify
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao aplicar ou verificar migrations. Confira o PostgreSQL e a connection string.' }
}

dotnet restore (Join-Path $repoRoot 'InovaGed.sln')
if ($LASTEXITCODE -ne 0) { throw 'Falha no restore dos pacotes .NET.' }

Write-Host ''
Write-Host 'Setup local concluido. Inicie com:' -ForegroundColor Green
Write-Host "  dotnet run --project `"$webProject`" --launch-profile https"
Write-Host 'Depois acesse https://localhost:7282/SystemHealth e valide banco, storage, OCR e workers.'
