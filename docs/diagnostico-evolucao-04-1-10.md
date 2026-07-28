# Diagnóstico Evolução 04.1.10

## SHA inicial

`00863c5ab5edf9011b3307baa6540063e5077e69`

## Gate inicial executado antes das alterações de código

| Comando | Resultado |
|---|---|
| `git rev-parse HEAD` | PASSOU: `00863c5ab5edf9011b3307baa6540063e5077e69` |
| `dotnet --info` | BLOQUEADO: `dotnet: command not found` no container |
| `dotnet sln InovaGed.sln list` | BLOQUEADO: `dotnet: command not found` no container |
| `dotnet clean InovaGed.sln` | BLOQUEADO: `dotnet: command not found` no container |
| `dotnet restore InovaGed.sln` | BLOQUEADO: `dotnet: command not found` no container |
| `dotnet build InovaGed.sln --no-restore --configuration Release` | BLOQUEADO: `dotnet: command not found` no container |
| `dotnet test InovaGed.sln --no-build --configuration Release` | BLOQUEADO: `dotnet: command not found` no container |
| `dotnet list InovaGed.sln package --include-transitive` | BLOQUEADO: `dotnet: command not found` no container |
| `git diff --check` | PASSOU antes das alterações |
| `gh workflow list` | BLOQUEADO: `gh: command not found` no container |
| `gh workflow view ci.yml` | BLOQUEADO: `gh: command not found` no container |
| `gh workflow view dotnet-ci.yml` | BLOQUEADO: `gh: command not found` no container |
| `gh run list --workflow ci.yml --limit 30` | BLOQUEADO: `gh: command not found` no container |
| `gh run list --workflow dotnet-ci.yml --limit 30` | BLOQUEADO: `gh: command not found` no container |
| `gh api repos/devmnsoft/inovaged/actions/permissions` | BLOQUEADO: `gh: command not found` no container |
| `gh api repos/devmnsoft/inovaged/branches/main/protection` | BLOQUEADO: `gh: command not found` no container |

## Estrutura da solução

A solução foi normalizada para encerrar com `EndGlobal` e não manter conteúdo duplicado após o bloco `Global`. Projetos esperados: Domain, Application, Infrastructure, Web MVC, WebApi, Operations Worker, Portability Verifier, Signing Agent e três projetos de teste.

## Workflows

Foram encontrados `ci.yml` e `dotnet-ci.yml`. Como `gh` não está instalado, não foi possível confirmar estado habilitado/desabilitado no GitHub. Foi adicionado workflow canônico `inovaged-ci.yml` com jobs: `actionlint`, `solution-and-build`, `server-integration`, `agent-windows`, `security`, `cms-e2e`, `poc-contract-tests` e `migration-matrix`.

## Erros e bloqueios

- Build/testes/migrations não puderam ser executados localmente por ausência do SDK .NET.
- Consultas de Actions e proteção de branch não puderam ser executadas localmente por ausência do GitHub CLI.
- Proteção da `main` deve ser aplicada externamente exigindo todos os checks do workflow `inovaged-ci` e revisão aprovada.

## Correções realizadas

- Solução canônica sem duplicidade após o `Global`.
- Teste automatizado para validar `dotnet sln InovaGed.sln list` e garantir cada projeto uma única vez.
- Workflow canônico de gate sistêmico.
- Guardas de segurança contra stubs produtivos e validações `VALID` artificiais.
- Remoção de `CertificateValidationStub` e `AllowAllPermissionChecker` da produção.
- Pairing do agente deixou de devolver o código para o site e passou a persistir pairings ativos via armazenamento protegido.
- DPAPI real em Windows com fallback apenas compatível para Linux/teste.

## Resultados finais locais

- `git diff --check`: executado após alterações e registrado na entrega final.
- Demais checks dependem de ambiente com .NET SDK, PostgreSQL, OpenSSL e GitHub Actions.
