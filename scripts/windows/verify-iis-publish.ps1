[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$PublishPath
)

$ErrorActionPreference = 'Stop'
$requiredFiles = @(
    'InovaGed.Web.dll',
    'web.config',
    'Npgsql.dll',
    'System.Diagnostics.DiagnosticSource.dll',
    'InovaGed.Web.deps.json'
)

if (-not (Test-Path -LiteralPath $PublishPath -PathType Container)) {
    throw "Publish IIS inválido: pasta não encontrada: $PublishPath"
}

$missing = @()
foreach ($file in $requiredFiles) {
    $fullPath = Join-Path $PublishPath $file
    if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
        Write-Host "[OK] $file encontrado" -ForegroundColor Green
    } else {
        Write-Error "[AUSENTE] Arquivo obrigatório: $fullPath" -ErrorAction Continue
        $missing += $file
    }
}

if ($missing.Count -gt 0) {
    throw "Publish IIS incompleto. Arquivos ausentes: $($missing -join ', '). Não promova este artefato."
}

$depsPath = Join-Path $PublishPath 'InovaGed.Web.deps.json'
$depsContent = Get-Content -LiteralPath $depsPath -Raw
if ($depsContent -notmatch 'System\.Diagnostics\.DiagnosticSource') {
    throw "InovaGed.Web.deps.json não referencia System.Diagnostics.DiagnosticSource. Refaça restore e publish."
}
Write-Host '[OK] InovaGed.Web.deps.json referencia System.Diagnostics.DiagnosticSource' -ForegroundColor Green

try {
    [xml](Get-Content -LiteralPath (Join-Path $PublishPath 'web.config') -Raw) | Out-Null
    Write-Host '[OK] web.config contém XML válido' -ForegroundColor Green
} catch {
    throw "web.config contém XML inválido: $($_.Exception.Message)"
}

Write-Host 'Publish IIS validado. Todos os arquivos críticos estão presentes.' -ForegroundColor Cyan
