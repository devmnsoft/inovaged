# Pastas, identidade e permissões do IIS — RC21

Execute em PowerShell elevado, substituindo `NOME_DO_APPPOOL`:

```powershell
New-Item -ItemType Directory -Force F:\Sistemas\ged\logs
New-Item -ItemType Directory -Force F:\InovaGed\temp
New-Item -ItemType Directory -Force F:\InovaGed\storage
icacls "F:\Sistemas\ged" /grant "IIS AppPool\NOME_DO_APPPOOL:(OI)(CI)M" /T
icacls "F:\InovaGed" /grant "IIS AppPool\NOME_DO_APPPOOL:(OI)(CI)M" /T
```

Conceda acesso apenas à identidade do AppPool. Não inclua `storage`, `uploads`, `logs`, certificados ou segredos no artefato/versionamento.

Configuração do AppPool:

- **.NET CLR Version:** No Managed Code
- **Managed Pipeline Mode:** Integrated
- **Enable 32-Bit Applications:** False

Confirme também a instalação do Hosting Bundle do .NET 8 e recicle o IIS após instalá-lo.
