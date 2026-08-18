[CmdletBinding()]
param([switch]$VerifyOnly)
$ErrorActionPreference = 'Stop'
if (-not $env:ConnectionStrings__DefaultConnection -and -not $env:DATABASE_URL) { throw 'Defina ConnectionStrings__DefaultConnection ou DATABASE_URL no ambiente; credenciais nao sao aceitas como parametro.' }
$root = Resolve-Path (Join-Path $PSScriptRoot '../..')
$verb = if ($VerifyOnly) { 'verify' } else { 'apply' }
dotnet run --project (Join-Path $root 'InovaGed.Database.Migrator') -c Release -- $verb --verify
if ($LASTEXITCODE) { throw 'Migrator falhou; a implantacao foi interrompida.' }
