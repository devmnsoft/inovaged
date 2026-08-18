[CmdletBinding(SupportsShouldProcess)]
param([string]$SiteName = 'InovaGed', [string]$PhysicalPath = 'C:\inetpub\InovaGed', [int]$Port = 8080)
$ErrorActionPreference = 'Stop'
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Execute como Administrador.' }
Import-Module WebAdministration
if ($PSCmdlet.ShouldProcess($SiteName, 'configurar IIS')) {
    Install-WindowsFeature Web-Server, Web-Mgmt-Tools | Out-Null
    New-Item $PhysicalPath -ItemType Directory -Force | Out-Null
    if (-not (Test-Path "IIS:\AppPools\$SiteName")) { New-WebAppPool $SiteName | Out-Null }
    Set-ItemProperty "IIS:\AppPools\$SiteName" managedRuntimeVersion ''
    if (-not (Test-Path "IIS:\Sites\$SiteName")) { New-Website -Name $SiteName -PhysicalPath $PhysicalPath -Port $Port -ApplicationPool $SiteName | Out-Null }
    & icacls $PhysicalPath /grant "IIS AppPool\${SiteName}:(OI)(CI)(RX)" /T | Out-Null
    Write-Host "[OK] IIS configurado. Dados gravaveis devem permanecer em C:\ProgramData\InovaGed." -ForegroundColor Green
}
