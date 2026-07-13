# Configuração por ambiente

## Regra geral
`appsettings.json` deve conter somente valores seguros e genéricos. Segredos entram por variável de ambiente, User Secrets, Docker Secret ou configuração do IIS.

## Desenvolvimento PowerShell
```powershell
cd C:\src\inovaged
 dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=inovaged;Username=inovaged_app;Password=<senha>" --project .\InovaGed.Web\InovaGed.Web.csproj
$env:INOVAGED_DEV_SEED_PASSWORD="<senha-temporaria>"
dotnet run --project .\InovaGed.Web\InovaGed.Web.csproj
```

## Windows Server/IIS
No `web.config` ou no painel do IIS, configure variáveis:
```powershell
[Environment]::SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "Host=db;Port=5432;Database=inovaged;Username=inovaged_app;Password=<senha>", "Machine")
[Environment]::SetEnvironmentVariable("Storage__Local__RootPath", "D:\InovaGED\storage", "Machine")
iisreset
```

## Docker
```powershell
docker run --rm -p 8080:8080 `
  -e ASPNETCORE_ENVIRONMENT=Production `
  -e ConnectionStrings__DefaultConnection="Host=postgres;Port=5432;Database=inovaged;Username=inovaged_app;Password=<senha>" `
  -e Storage__Local__RootPath="/var/lib/inovaged/storage" `
  devmnsoft/inovaged:stable
```

## Produção
- `SystemSeed:Enabled=false`.
- `Auth:AllowInternalSelfSignedCertificates=false`.
- `SchemaRepair:Enabled=false`.
- `AllowedHosts` deve listar hosts reais.
- Use HTTPS no proxy/IIS.
