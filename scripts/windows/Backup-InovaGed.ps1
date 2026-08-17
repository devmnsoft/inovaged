[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$DatabaseUrl,
    [string]$OutputDirectory = 'C:\InovaGed\backups'
)

$ErrorActionPreference = 'Stop'
if (-not (Get-Command pg_dump -ErrorAction SilentlyContinue) -or -not (Get-Command pg_restore -ErrorAction SilentlyContinue)) {
    throw 'pg_dump/pg_restore nao encontrados. Adicione a pasta bin do PostgreSQL ao PATH.'
}

$fullRoot = [IO.Path]::GetFullPath($OutputDirectory)
if ($fullRoot -match '(?i)[\\/]wwwroot([\\/]|$)') { throw 'O diretorio de backup nao pode estar dentro de wwwroot.' }
New-Item -ItemType Directory -Force -Path $fullRoot | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$destination = Join-Path $fullRoot "inovaged-$stamp.dump"
$partial = "$destination.partial"

try {
    & pg_dump --format=custom --compress=6 --no-owner --no-privileges --file=$partial --dbname=$DatabaseUrl
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $partial)) { throw 'pg_dump falhou. Nenhum backup valido foi produzido.' }
    & pg_restore --list $partial | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'O arquivo gerado nao passou na validacao do pg_restore.' }
    Move-Item -LiteralPath $partial -Destination $destination -Force
    $hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "Backup concluido: $destination" -ForegroundColor Green
    Write-Host "SHA-256: $hash"
} finally {
    Remove-Item -LiteralPath $partial -Force -ErrorAction SilentlyContinue
}
