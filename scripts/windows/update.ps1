[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [string]$SiteName = 'InovaGed',
    [string]$InstallRoot = 'C:\inetpub\InovaGed',
    [string]$DataRoot = 'C:\ProgramData\InovaGed',
    [string]$HealthUrl = 'http://localhost:8080/health'
)
$ErrorActionPreference = 'Stop'
$package = (Resolve-Path $PackageDirectory).Path
if (-not (Test-Path (Join-Path $package 'InovaGed.Web.dll'))) { throw 'Pacote invalido: InovaGed.Web.dll ausente.' }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$releases = Join-Path $InstallRoot 'releases'; $release = Join-Path $releases $stamp
$current = Join-Path $InstallRoot 'current'; $previous = Join-Path $InstallRoot 'previous'
$logDir = Join-Path $DataRoot 'logs'; New-Item $logDir,$releases -ItemType Directory -Force | Out-Null
Start-Transcript -Path (Join-Path $logDir "update-$stamp.log") | Out-Null
try {
    & (Join-Path $PSScriptRoot 'check-environment.ps1') -PublishDirectory $InstallRoot -DataRoot $DataRoot
    & (Join-Path $PSScriptRoot 'backup-db.ps1') -OutputDirectory (Join-Path $DataRoot 'backups')
    & (Join-Path $PSScriptRoot 'backup-files.ps1') -UploadsDirectory (Join-Path $DataRoot 'uploads') -OutputDirectory (Join-Path $DataRoot 'backups')
    Copy-Item $package $release -Recurse
    & (Join-Path $PSScriptRoot 'apply-migrations.ps1')
    Import-Module WebAdministration; Stop-WebAppPool $SiteName -ErrorAction SilentlyContinue
    Remove-Item $previous -Recurse -Force -ErrorAction SilentlyContinue
    if (Test-Path $current) { Move-Item $current $previous }
    Move-Item $release $current
    Set-ItemProperty "IIS:\Sites\$SiteName" physicalPath $current
    Start-WebAppPool $SiteName
    Start-Sleep 5
    $response = Invoke-WebRequest $HealthUrl -UseBasicParsing -TimeoutSec 20
    if ($response.StatusCode -notin 200,204) { throw "Health check retornou $($response.StatusCode)." }
    Write-Host '[OK] Atualizacao concluida; configuracoes e uploads externos foram preservados.' -ForegroundColor Green
} finally { Stop-Transcript -ErrorAction SilentlyContinue | Out-Null }
