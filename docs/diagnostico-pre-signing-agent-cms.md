# Diagnóstico pré Signing Agent CMS

- SHA inicial: `bf0f985e1d313d083d82ad96b35442905ee7b174`
- Branch de trabalho: `codex/implementar-signing-agent-cms-runtime`

## Comandos executados
- `dotnet --info`
- `dotnet clean InovaGed.sln`
- `dotnet restore InovaGed.sln`
- `dotnet build InovaGed.sln --no-restore --configuration Release`
- `dotnet test InovaGed.sln --no-build --configuration Release`
- `dotnet sln InovaGed.sln list`
- `dotnet list InovaGed.sln package --include-transitive`
- `git diff --check`

## Resultado
- `/bin/bash: line 3: dotnet: command not found`
- `/bin/bash: line 3: dotnet: command not found`
- `/bin/bash: line 3: dotnet: command not found`
- `/bin/bash: line 3: dotnet: command not found`
- `/bin/bash: line 3: dotnet: command not found`
- `/bin/bash: line 3: dotnet: command not found`
- `/bin/bash: line 3: dotnet: command not found`

## Falha preexistente
O SDK/CLI `dotnet` não está instalado no container de execução, portanto restore, build, testes, validação da solution e inicialização dos hosts MVC/WebApi não puderam ser executados localmente.

## Causa
Limitação do ambiente local desta sessão, anterior às alterações de código.

## Correção recomendada
Executar os comandos em runner Linux/Windows com .NET 8 instalado.

## Risco residual
A validação completa depende do CI. Nenhuma falha foi ocultada com `continue-on-error`, `|| true` ou filtros de teste.
