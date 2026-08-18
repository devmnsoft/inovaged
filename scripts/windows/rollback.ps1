[CmdletBinding(SupportsShouldProcess, ConfirmImpact='High')]
param([string]$SiteName = 'InovaGed', [string]$InstallRoot = 'C:\inetpub\InovaGed', [string]$DataRoot = 'C:\ProgramData\InovaGed', [string]$HealthUrl = 'http://localhost:8080/health')
$ErrorActionPreference = 'Stop'
$current = Join-Path $InstallRoot 'current'; $previous = Join-Path $InstallRoot 'previous'
if (-not (Test-Path (Join-Path $previous 'InovaGed.Web.dll'))) { throw 'Publicacao anterior valida nao encontrada.' }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'; $logDir = Join-Path $DataRoot 'logs'; New-Item $logDir -ItemType Directory -Force | Out-Null
Start-Transcript -Path (Join-Path $logDir "rollback-$stamp.log") | Out-Null
try {
    if ($PSCmdlet.ShouldProcess($SiteName, 'restaurar publicacao anterior sem alterar uploads')) {
        Import-Module WebAdministration; Stop-WebAppPool $SiteName -ErrorAction SilentlyContinue
        $failed = Join-Path (Join-Path $InstallRoot 'releases') "failed-$stamp"
        if (Test-Path $current) { Move-Item $current $failed }
        Move-Item $previous $current; Set-ItemProperty "IIS:\Sites\$SiteName" physicalPath $current; Start-WebAppPool $SiteName
        Start-Sleep 5; $response = Invoke-WebRequest $HealthUrl -UseBasicParsing -TimeoutSec 20
        if ($response.StatusCode -notin 200,204) { throw 'Publicacao anterior iniciou sem health check saudavel.' }
        Write-Warning 'Rollback binario concluido. O banco NAO foi revertido. Restaure-o somente se a matriz de compatibilidade exigir, em janela controlada.'
    }
} finally { Stop-Transcript -ErrorAction SilentlyContinue | Out-Null }
