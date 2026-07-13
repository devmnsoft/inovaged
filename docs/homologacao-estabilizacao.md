# Homologação da estabilização

## Checklist
```powershell
dotnet clean .\InovaGed.sln
dotnet restore .\InovaGed.sln
dotnet build .\InovaGed.sln --no-restore
dotnet test .\InovaGed.sln --no-build
```

## Validações manuais
- Subir Production sem connection string deve falhar com mensagem controlada.
- `/SystemHealth/SecurityConfiguration` não deve exibir senha, token ou chave.
- `/SystemHealth/Workers` deve mostrar apenas dados persistidos reais.
- Seeds só executam em Development/PoC com `INOVAGED_DEV_SEED_PASSWORD`.
