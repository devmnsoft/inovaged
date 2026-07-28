# NETSDK1141

## Sintoma e causa
O erro ocorre quando `global.json` pede uma versão que nenhum SDK instalado pode resolver. A configuração anterior, `8.0.423` com `latestPatch`, ficava presa à feature band 8.0.4xx. SDK e `TargetFramework` são decisões distintas: os projetos permanecem em `net8.0` e o SDK oficial também permanece .NET 8.

## Verificar e instalar no Windows
```powershell
dotnet --list-sdks
dotnet --version
where.exe dotnet
Get-ChildItem "C:\Program Files\dotnet\sdk"
winget install --id Microsoft.DotNet.SDK.8 --source winget
.\eng\verify-dotnet-sdk.ps1
```
Feche o Visual Studio antes da instalação e abra-o novamente depois. O script deve selecionar `8.0.x`.

## Limpeza segura e build
Na raiz do repositório, remova somente `.vs`, `bin` e `obj`:
```powershell
dotnet clean InovaGed.sln
Get-ChildItem -Recurse -Directory | Where-Object { $_.Name -in @("bin", "obj") } | Remove-Item -Recurse -Force
.\eng\build.ps1
```
Não remova código, configurações locais, storage, banco, user secrets ou certificados.
