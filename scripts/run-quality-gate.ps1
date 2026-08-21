$ErrorActionPreference = "Stop"
Push-Location (Join-Path $PSScriptRoot "..")
try {
  dotnet clean InovaGed.sln
  dotnet restore InovaGed.sln
  dotnet build InovaGed.sln -v:minimal
  dotnet run --project InovaGed.Environment.Doctor --no-build -- quality-gate @args
} finally { Pop-Location }
