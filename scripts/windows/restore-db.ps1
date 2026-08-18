[CmdletBinding(SupportsShouldProcess, ConfirmImpact='High')]
param([Parameter(Mandatory)][string]$BackupFile, [Parameter(Mandatory)][string]$Confirmation)
$ErrorActionPreference = 'Stop'
$database = if ($env:DATABASE_URL) { $env:DATABASE_URL } else { $env:ConnectionStrings__DefaultConnection }
if (-not $database) { throw 'Defina a conexao no ambiente.' }
if ($PSCmdlet.ShouldProcess('PostgreSQL', 'restaurar backup')) { & (Join-Path $PSScriptRoot 'Restore-InovaGed.ps1') -BackupFile $BackupFile -DatabaseUrl $database -Confirmation $Confirmation -Confirm:$false }
