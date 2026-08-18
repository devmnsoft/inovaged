[CmdletBinding()]
param([string]$OutputDirectory = 'C:\ProgramData\InovaGed\backups')
$ErrorActionPreference = 'Stop'
$database = if ($env:DATABASE_URL) { $env:DATABASE_URL } else { $env:ConnectionStrings__DefaultConnection }
if (-not $database) { throw 'Defina DATABASE_URL ou ConnectionStrings__DefaultConnection no ambiente.' }
& (Join-Path $PSScriptRoot 'Backup-InovaGed.ps1') -DatabaseUrl $database -OutputDirectory $OutputDirectory
if ($LASTEXITCODE) { throw 'Backup do banco falhou.' }
