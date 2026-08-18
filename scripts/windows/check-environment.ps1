[CmdletBinding()]
param(
    [string]$PublishDirectory = 'C:\inetpub\InovaGed',
    [string]$DataRoot = 'C:\ProgramData\InovaGed',
    [switch]$RequireOcr
)

$ErrorActionPreference = 'Stop'
$failures = [Collections.Generic.List[string]]::new()
function Assert-Check([bool]$Condition, [string]$Message) {
    if ($Condition) { Write-Host "[OK] $Message" -ForegroundColor Green }
    else { $failures.Add($Message); Write-Host "[CRITICO] $Message" -ForegroundColor Red }
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
Assert-Check ($null -ne $dotnet) '.NET 8/Hosting Bundle disponivel'
if ($dotnet) { Assert-Check ((& dotnet --list-runtimes) -match 'Microsoft.AspNetCore.App 8\.') 'ASP.NET Core Runtime 8 instalado' }
Assert-Check ($null -ne (Get-WindowsOptionalFeature -Online -FeatureName IIS-WebServerRole -ErrorAction SilentlyContinue | Where-Object State -eq Enabled)) 'IIS habilitado'

$connectionString = [Environment]::GetEnvironmentVariable('ConnectionStrings__DefaultConnection', 'Machine')
if (-not $connectionString) { $connectionString = $env:ConnectionStrings__DefaultConnection }
Assert-Check (-not [string]::IsNullOrWhiteSpace($connectionString)) 'ConnectionStrings__DefaultConnection configurada no ambiente'
Assert-Check ($null -ne (Get-Command psql -ErrorAction SilentlyContinue)) 'Cliente PostgreSQL disponivel'
if ($connectionString -and (Get-Command psql -ErrorAction SilentlyContinue)) {
    & psql $connectionString -X --no-psqlrc --tuples-only --command 'select 1' 1>$null
    Assert-Check ($LASTEXITCODE -eq 0) 'PostgreSQL acessivel'
}

foreach ($path in @($PublishDirectory, (Join-Path $DataRoot 'uploads'), (Join-Path $DataRoot 'logs'))) {
    New-Item -ItemType Directory -Path $path -Force | Out-Null
    $probe = Join-Path $path ".probe-$([guid]::NewGuid().ToString('N'))"
    try { Set-Content $probe '' -NoNewline; Assert-Check $true "Diretorio gravavel: $path" } catch { Assert-Check $false "Diretorio sem permissao de escrita: $path" } finally { Remove-Item $probe -Force -ErrorAction SilentlyContinue }
}

if ($RequireOcr) {
    Assert-Check ($null -ne (Get-Command tesseract -ErrorAction SilentlyContinue)) 'Tesseract configurado'
    Assert-Check ($null -ne (Get-Command ocrmypdf -ErrorAction SilentlyContinue)) 'OCRmyPDF configurado'
}
if ($failures.Count) { throw "Ambiente invalido: $($failures.Count) verificacao(oes) critica(s)." }
