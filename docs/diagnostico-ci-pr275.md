# Diagnóstico do CI — PR #275

## Workflow, job e step

- Workflow: `ci` e `dotnet-ci`.
- Job: `build-test` / `restore-build-test`.
- Step afetado: `Architecture and DI guard`.
- Comando original: `dotnet test InovaGed.Application.Tests/InovaGed.Application.Tests.csproj --no-restore --configuration Release --filter "FullyQualifiedName~SolutionAndContinuityArchitectureTests|FullyQualifiedName~DependencyInjectionCompositionTests"`.

## Mensagem completa observada localmente

A execução local não pôde reproduzir o runner por indisponibilidade do SDK no contêiner:

```text
/bin/bash: line 1: dotnet: command not found
```

A análise estática do commit `b495c1e` mostrou a causa provável do step antes de qualquer alteração: o teste `Solution_has_unique_projects_and_no_content_after_EndGlobal` exigia `EndGlobal\n` e ausência absoluta de conteúdo após `EndGlobal`, uma validação de formatação frágil da solution. O mesmo arquivo também validava SQL dentro de `InovaGed.Application/Administration/PermissionEnforcement.cs`, enquanto a regra arquitetural correta exige SQL em Infrastructure.

## Causa raiz

O guard arquitetural misturava três preocupações:

1. formato físico da solution (quebra de linha final obrigatória);
2. presença de projetos;
3. detalhes internos de SQL dentro da camada Application.

Isso fazia o CI falhar no step arquitetural antes de o build Release expor erros de compilação e reforçava uma violação arquitetural real: `DatabasePermissionChecker` com Dapper/SQL na camada Application.

## Arquivos envolvidos

- `.github/workflows/ci.yml`.
- `.github/workflows/dotnet-ci.yml`.
- `InovaGed.Application.Tests/SolutionAndContinuityArchitectureTests.cs`.
- `InovaGed.Application/Administration/PermissionEnforcement.cs`.
- `InovaGed.Infrastructure/Security/DatabasePermissionChecker.cs`.
- `InovaGed.Infrastructure/DependencyInjection.cs`.

## Correção

- O pipeline canônico agora executa restore, árvore de dependências, build Release e só depois o guard arquitetural, sempre produzindo logs/TRX.
- `dotnet-ci` ficou restrito a guards especializados para evitar duplicação da mesma sequência completa.
- O teste arquitetural deixou de depender de quebra de linha final e passou a validar presença única de projetos, ausência de banco no Domain, contrato de permissão sem SQL/Dapper em Application e implementação tenant-scoped em Infrastructure.
- `DatabasePermissionChecker` foi movido para `InovaGed.Infrastructure/Security`.

## Evidência posterior

No contêiner atual, as validações `dotnet` não foram executadas porque o SDK não está instalado. Foram executadas as validações estáticas possíveis (`git diff --check`, inspeção com `rg` e revisão dos workflows). A evidência completa em runner GitHub Actions dependerá do ambiente com .NET 8 disponível.
