[CmdletBinding()]
param([string]$OutputDirectory = 'artifacts\publish', [string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '../..')
$output = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
if ($output.StartsWith((Join-Path $root 'InovaGed.Web'), [StringComparison]::OrdinalIgnoreCase)) { throw 'A publicacao nao pode ficar dentro do projeto Web.' }
Remove-Item $output -Recurse -Force -ErrorAction SilentlyContinue
dotnet restore (Join-Path $root 'InovaGed.sln')
if ($LASTEXITCODE) { throw 'Restore falhou.' }
dotnet publish (Join-Path $root 'InovaGed.Web/InovaGed.Web.csproj') -c Release -r $Runtime --self-contained false --no-restore -o $output
if ($LASTEXITCODE -or -not (Test-Path (Join-Path $output 'InovaGed.Web.dll'))) { throw 'Publicacao invalida.' }
Get-ChildItem $output -Recurse -File | Get-FileHash -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLowerInvariant())  $($_.Path.Substring($output.Length + 1))" } | Set-Content (Join-Path $output 'checksums.sha256')
Write-Host "[OK] Build publicado em $output" -ForegroundColor Green
