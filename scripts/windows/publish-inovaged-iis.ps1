[CmdletBinding()]
param(
    [string]$OutputPath = 'F:\Sistemas\ged_publish_temp'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$solution = Join-Path $repoRoot 'InovaGed.sln'
$webProject = Join-Path $repoRoot 'InovaGed.Web\InovaGed.Web.csproj'
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)

# Defesa contra remoção da pasta real: somente destinos cujo nome explicita uso temporário.
$leaf = Split-Path $resolvedOutput -Leaf
if ($leaf -notmatch '(?i)(publish[_-]?temp|temp[_-]?publish)') {
    throw "Destino recusado: '$resolvedOutput'. Use exclusivamente uma pasta temporária de publish (ex.: ged_publish_temp), nunca a raiz do site IIS."
}

if (Test-Path -LiteralPath $resolvedOutput) {
    Write-Host "Removendo somente a pasta temporária: $resolvedOutput" -ForegroundColor Yellow
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null

Push-Location $repoRoot
try {
    & dotnet clean $solution
    if ($LASTEXITCODE) { throw 'dotnet clean falhou.' }
    & dotnet nuget locals all --clear
    if ($LASTEXITCODE) { throw 'dotnet nuget locals falhou.' }
    & dotnet restore $solution
    if ($LASTEXITCODE) { throw 'dotnet restore falhou.' }
    & dotnet build $solution -c Release '-v:minimal' --no-restore
    if ($LASTEXITCODE) { throw 'dotnet build falhou.' }
    & dotnet publish $webProject -c Release -o $resolvedOutput --no-build
    if ($LASTEXITCODE) { throw 'dotnet publish falhou.' }

    & (Join-Path $PSScriptRoot 'verify-iis-publish.ps1') -PublishPath $resolvedOutput
    if ($LASTEXITCODE) { throw 'A verificação do publish falhou.' }
} finally {
    Pop-Location
}

Write-Host ''
Write-Host 'Checklist final:' -ForegroundColor Cyan
foreach ($file in @('InovaGed.Web.dll','web.config','Npgsql.dll','System.Diagnostics.DiagnosticSource.dll','InovaGed.Web.deps.json')) {
    Write-Host "  [OK] $file"
}
Write-Host "Artefato pronto em $resolvedOutput. Nenhum arquivo foi copiado para produção." -ForegroundColor Green
Write-Host 'Revise o checklist e confirme manualmente antes de promover para o IIS.' -ForegroundColor Yellow
