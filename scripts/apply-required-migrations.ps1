$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
dotnet run --project InovaGed.Environment.Doctor -- database-readiness
if ((Read-Host 'Aplicar as migrations obrigatórias pendentes? [y/N]') -match '^[Yy]$') {
    dotnet run --project InovaGed.Environment.Doctor -- apply-required-migrations
}
