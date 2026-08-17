[CmdletBinding()]
param(
    [switch]$RequireOcr,
    [switch]$RequirePostgresTools
)

$ErrorActionPreference = 'Stop'
$failures = [System.Collections.Generic.List[string]]::new()

function Test-Command {
    param([string]$Name, [string]$InstallHint, [switch]$Required)

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($command) {
        $version = (& $command.Source --version 2>&1 | Select-Object -First 1)
        Write-Host "[OK] $Name - $version" -ForegroundColor Green
        return
    }

    $message = "$Name nao encontrado. $InstallHint"
    if ($Required) {
        $failures.Add($message)
        Write-Host "[ERRO] $message" -ForegroundColor Red
    } else {
        Write-Host "[AVISO] $message" -ForegroundColor Yellow
    }
}

Test-Command dotnet 'Instale o .NET SDK 8 (x64): https://dotnet.microsoft.com/download/dotnet/8.0' -Required
Test-Command psql 'Instale o PostgreSQL 15 ou superior e adicione a pasta bin ao PATH.' -Required:$RequirePostgresTools
Test-Command pg_dump 'Adicione a pasta bin do PostgreSQL ao PATH; ela e necessaria para backup.' -Required:$RequirePostgresTools
Test-Command pg_restore 'Adicione a pasta bin do PostgreSQL ao PATH; ela e necessaria para restauracao.' -Required:$RequirePostgresTools
Test-Command ocrmypdf 'Execute: py -m pip install ocrmypdf (Ghostscript e Tesseract tambem sao necessarios).' -Required:$RequireOcr
Test-Command tesseract 'Instale o Tesseract OCR e o pacote de idioma por.' -Required:$RequireOcr
Test-Command gswin64c 'Instale o Ghostscript x64 e adicione sua pasta bin ao PATH.' -Required:$RequireOcr

if ($failures.Count -gt 0) {
    Write-Error ("Pre-requisitos obrigatorios ausentes:`n - " + ($failures -join "`n - "))
    exit 1
}

Write-Host 'Pre-requisitos obrigatorios validados.' -ForegroundColor Green
