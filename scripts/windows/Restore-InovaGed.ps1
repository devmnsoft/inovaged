[CmdletBinding(SupportsShouldProcess, ConfirmImpact='High')]
param(
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string]$BackupFile,
    [Parameter(Mandatory)][string]$DatabaseUrl,
    [Parameter(Mandatory)][string]$Confirmation
)

$ErrorActionPreference = 'Stop'
if ($Confirmation -cne 'RESTAURAR') { throw 'Restauracao cancelada. Informe -Confirmation RESTAURAR explicitamente.' }
if (-not (Get-Command pg_restore -ErrorAction SilentlyContinue)) { throw 'pg_restore nao encontrado no PATH.' }

$resolvedBackup = (Resolve-Path -LiteralPath $BackupFile).Path
& pg_restore --list $resolvedBackup | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Backup invalido ou corrompido; a restauracao nao foi iniciada.' }

if ($PSCmdlet.ShouldProcess('banco PostgreSQL de destino', "restaurar $resolvedBackup")) {
    & pg_restore --clean --if-exists --no-owner --no-privileges --exit-on-error --dbname=$DatabaseUrl $resolvedBackup
    if ($LASTEXITCODE -ne 0) { throw 'Restauracao interrompida pelo pg_restore. Consulte a saida acima.' }
    Write-Host 'Restauracao concluida. Execute novamente as migrations e valide /SystemHealth.' -ForegroundColor Green
}
