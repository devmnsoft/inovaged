# Evidências de teste

| Comando | Resultado local |
|---|---|
| `dotnet --info` | Não executado: binário `dotnet` ausente no contêiner. |
| `dotnet build InovaGed.Infrastructure/InovaGed.Infrastructure.csproj --configuration Release` | Não executado pelo mesmo limite. |
| `dotnet build InovaGed.sln --configuration Release` | Não executado pelo mesmo limite. |
| `git diff --check` | Aprovado. |
| contagem estrutural de `{`/`}` por arquivo de continuidade | Aprovada; contagens equilibradas. |

O job `continuity-integration` instala .NET 8, inicia PostgreSQL 16, aplica migrations, executa testes filtrados e publica TRX/logs. Seu resultado remoto ainda não existe; por isso a PR deve permanecer draft.
