# Diagnóstico CMS Agent ponta a ponta

- Branch criada: `codex/finalizar-cms-agent-end-to-end`.
- SHA inicial: `6c1fcbe627c97fee97b472fa732616ba8c61300c`.
- Atualização com `main`: bloqueada porque o repositório local não possui remote `origin` configurado; `git fetch origin main` retornou erro de remote inexistente.
- Workflows encontrados: `.github/workflows/ci.yml` e `.github/workflows/dotnet-ci.yml`.
- Causa provável do `ci` não aparecer no PR 280: antes desta evolução o workflow principal expunha jobs com nomes `build-test` e `signing-agent-windows`, enquanto os critérios esperados exigem jobs obrigatórios `server-linux`, `agent-windows` e `security-guards`.
- Sintaxe YAML: revisão estática aplicada em `ci.yml`; validação executável por GitHub Actions não pôde ser rodada localmente nesta imagem.
- `dotnet --info`: falhou localmente porque o SDK `dotnet` não está instalado no container.
- `dotnet clean InovaGed.sln`: não executado pelo mesmo bloqueio.
- `dotnet restore InovaGed.sln`: não executado pelo mesmo bloqueio.
- `dotnet build InovaGed.sln --no-restore --configuration Release`: não executado pelo mesmo bloqueio.
- `dotnet test InovaGed.sln --no-build --configuration Release`: não executado pelo mesmo bloqueio.
- `dotnet sln InovaGed.sln list`: não executado pelo mesmo bloqueio.
- `dotnet list InovaGed.sln package --include-transitive`: não executado pelo mesmo bloqueio.
- `git diff --check`: executado ao final da alteração.

## Correções aplicadas

- `ci.yml` passou a declarar os jobs obrigatórios `server-linux`, `agent-windows` e `security-guards`.
- DTOs públicos foram separados dos comandos internos sensíveis.
- DI em modo `DigitalSignature.Enabled=true` e `Mode=AgentCms` passou a registrar repositórios PostgreSQL em vez de Noop.
- Validação CMS deixou de colapsar certificado expirado/não vigente/sem Key Usage em `VALID` geral.
- Identidade do certificado passou a usar HMAC-SHA-256 configurável para CPF pesquisável.
