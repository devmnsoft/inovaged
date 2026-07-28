$ErrorActionPreference = 'Stop'; $root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path; Push-Location $root
try {
  & "$PSScriptRoot\verify-dotnet-sdk.ps1"
  dotnet clean InovaGed.sln
  dotnet restore InovaGed.sln --locked-mode
  dotnet build InovaGed.Application/InovaGed.Application.csproj --no-restore -c Release
  dotnet build InovaGed.Infrastructure/InovaGed.Infrastructure.csproj --no-restore -c Release
  dotnet build InovaGed.Web/InovaGed.Web.csproj --no-restore -c Debug
  dotnet build InovaGed.Web/InovaGed.Web.csproj --no-restore -c Release
  dotnet build InovaGed.sln --no-restore -c Release
  dotnet test InovaGed.sln --no-build -c Release
  dotnet publish InovaGed.Web/InovaGed.Web.csproj --no-build -c Release -o artifacts/web
} finally { Pop-Location }
