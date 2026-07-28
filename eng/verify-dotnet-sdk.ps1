$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$globalPath = Join-Path $root 'global.json'
function Fail-Toolchain([string]$Selected = 'não detectado', [int]$Code = 1) {
  Write-Error @"
Toolchain do InovaGED não encontrada.

Framework de destino: net8.0
SDK necessário: .NET SDK 8.0
SDK selecionado: $Selected

Instale o SDK com:
winget install --id Microsoft.DotNet.SDK.8 --source winget

Depois:
1. Feche o Visual Studio.
2. Abra novamente.
3. Execute dotnet --list-sdks.
4. Execute eng\verify-dotnet-sdk.ps1.
"@ -ErrorAction Continue
  exit $Code
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Fail-Toolchain }
if (-not (Test-Path $globalPath)) { Fail-Toolchain 'global.json ausente' 2 }
try { $global = Get-Content $globalPath -Raw | ConvertFrom-Json } catch { Fail-Toolchain 'global.json inválido' 2 }
if ($global.sdk.version -ne '8.0.100' -or $global.sdk.rollForward -ne 'latestFeature' -or $global.sdk.allowPrerelease -ne $false) { Fail-Toolchain 'global.json fora do contrato' 2 }
$sdks = @(& dotnet --list-sdks)
$selected = (& dotnet --version 2>$null)
if (-not ($sdks | Where-Object { $_ -match '^8\.0\.\d+\s' })) { Fail-Toolchain $selected }
if ($selected -notmatch '^8\.0\.\d+$') { Fail-Toolchain $selected }
Write-Host "Toolchain do InovaGED validada: SDK $selected (global.json: 8.0.100/latestFeature)."
