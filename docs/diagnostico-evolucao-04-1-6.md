# Diagnóstico Evolução 04.1.6

- SHA inicial registrado: `3bf2366ff4e9e38cdd87bab71f5c42f2131819fa`.
- Branch de trabalho: `codex/estabilizar-homologar-cms-final`.
- Workflows encontrados: `.github/workflows/ci.yml` e `.github/workflows/dotnet-ci.yml`.
- Causa provável da ausência do CI nos PRs anteriores: o workflow principal depende de GitHub Actions e regras externas de branch protection que não são versionáveis no repositório. O arquivo `ci.yml` já existia, mas foi reforçado para `workflow_dispatch`, `pull_request` e `push` em `main`, `develop`, `feature/**` e `codex/**`.
- Configurações externas que o administrador deve validar: Actions habilitado no repositório, execução permitida para pull requests, checks obrigatórios configurados para `server-linux`, `agent-windows` e `security-guards`, e PR mantida em draft até a execução real desses checks.
- Ambiente local: `dotnet` não está instalado no contêiner, portanto `dotnet --info`, restore, build, test e listagem da solução não puderam executar localmente.
- Migrations: o orquestrador foi ajustado para inclusões `\\ir` relativas ao próprio arquivo e para reaplicação a partir de qualquer diretório no workflow.
- Evidências finais locais: `git diff --check` executado sem erros após as alterações.

## Comandos executados no gate inicial

```bash
dotnet --info
# /bin/bash: dotnet: command not found

dotnet clean InovaGed.sln
# /bin/bash: dotnet: command not found

dotnet restore InovaGed.sln
# /bin/bash: dotnet: command not found

dotnet build InovaGed.sln --no-restore --configuration Release
# /bin/bash: dotnet: command not found

dotnet test InovaGed.sln --no-build --configuration Release
# /bin/bash: dotnet: command not found

dotnet sln InovaGed.sln list
# /bin/bash: dotnet: command not found

dotnet list InovaGed.sln package --include-transitive
# /bin/bash: dotnet: command not found

git diff --check
# sem saída
```
