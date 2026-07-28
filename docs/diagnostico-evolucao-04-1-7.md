# Diagnóstico Evolução 04.1.7

- Data UTC: 2026-07-23.
- Branch alvo: codex/cms-release-candidate-homologacao-real.
- SHA inicial: eb02542a0c9258f8bd918fb324d694872c1bd81e.
- Remote: não configurado; `git fetch origin main` falhou porque `origin` não existe neste checkout.

## Gate inicial executado

- `dotnet --info`: falhou, `dotnet: command not found`.
- `dotnet clean InovaGed.sln`: falhou, `dotnet: command not found`.
- `dotnet restore InovaGed.sln`: falhou, `dotnet: command not found`.
- `dotnet build InovaGed.sln --no-restore --configuration Release`: falhou, `dotnet: command not found`.
- `dotnet test InovaGed.sln --no-build --configuration Release`: falhou, `dotnet: command not found`.
- `dotnet sln InovaGed.sln list`: falhou, `dotnet: command not found`.
- `dotnet list InovaGed.sln package --include-transitive`: falhou, `dotnet: command not found`.
- `git diff --check`: executado no gate, sem alterações no momento do gate.
- `gh workflow list`: falhou, `gh: command not found`.
- `gh workflow view ci.yml`: falhou, `gh: command not found`.
- `gh run list --workflow ci.yml --limit 20`: falhou, `gh: command not found`.
- `gh run list --workflow dotnet-ci.yml --limit 20`: falhou, `gh: command not found`.

## Workflows

O arquivo `.github/workflows/ci.yml` já define os jobs obrigatórios `actionlint`, `server-linux`, `agent-windows` e `security-guards`, com gatilhos em `workflow_dispatch`, `pull_request` e `push` para `main`, `develop`, `feature/**` e `codex/**`.

O arquivo `.github/workflows/dotnet-ci.yml` contém apenas `specialized-guards`; portanto não deve ser usado como evidência de CI verde para a evolução 04.1.7.

## Configuração externa necessária

- GitHub Actions > Rulesets/Branch protection rules > branch `main`: exigir checks `actionlint`, `server-linux`, `agent-windows` e `security-guards`.
- Runner Linux com SDK .NET 8, PostgreSQL e OpenSSL.
- Runner Windows com SDK .NET 8 e permissões CurrentUser para store de certificados.
- Ambiente local deste agente não possui SDK .NET nem GitHub CLI; build, testes, migrations e inspeção remota de runs não puderam ser comprovados aqui.

## Correções aplicadas

- Contrato principal `ISigningOrchestrator` passa a receber `PrepareSigningSessionCommand` e `CompleteSigningSessionCommand` tipados.
- Fluxo CMS do controller deixou de converter conclusão para `CompleteSignatureCommand` com `TechnicalMetadata`.
- Criação da sessão CMS foi movida para o orquestrador de aplicação/infraestrutura, que calcula hash real, gera tokens, define TTL, persiste e só então retorna a sessão.
- Contrato legado `CompleteSignatureCommand` foi marcado como obsoleto para uso exclusivo por providers antigos.
- Introduzida abstração `ISigningUnitOfWork` e implementação PostgreSQL para orientar a conclusão transacional em uma conexão/transação.

## Resultados finais

- `git diff --check`: executado após as alterações e passou.
- Build/testes/migrations/CI: pendentes de ambiente com `dotnet`, `gh`, PostgreSQL e runners GitHub Actions.
