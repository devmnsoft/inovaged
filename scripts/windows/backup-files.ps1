[CmdletBinding()]
param([string]$UploadsDirectory = 'C:\ProgramData\InovaGed\uploads', [string]$OutputDirectory = 'C:\ProgramData\InovaGed\backups')
$ErrorActionPreference = 'Stop'
if (-not (Test-Path $UploadsDirectory -PathType Container)) { throw "Uploads nao encontrado: $UploadsDirectory" }
New-Item $OutputDirectory -ItemType Directory -Force | Out-Null
$destination = Join-Path $OutputDirectory "uploads-$(Get-Date -Format 'yyyyMMdd-HHmmss').zip"
Compress-Archive -Path (Join-Path $UploadsDirectory '*') -DestinationPath $destination -CompressionLevel Optimal
if (-not (Test-Path $destination) -or (Get-Item $destination).Length -eq 0) { throw 'Backup de uploads invalido.' }
Write-Host "[OK] Uploads preservados em $destination" -ForegroundColor Green
