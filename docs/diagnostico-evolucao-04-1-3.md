# Diagnóstico — Evolução 04.1.3

- SHA inicial: `b806ad68ba9d2d4cf6a430a882097ded70070386`.
- Branch de trabalho: `codex/finalizar-cms-e2e-homologacao`.
- Workflow principal encontrado: `.github/workflows/ci.yml`.
- Causa operacional identificada para lacunas de execução nos PRs anteriores: o workflow não tinha `workflow_dispatch` e a validação de migrations era apenas estática, o que permitia PRs sem comprovação de aplicação/reaplicação real em PostgreSQL.
- Workflows habilitados nesta evolução: `pull_request`, `push` para `main`, `develop`, `feature/**`, `codex/**` e execução manual.
- Validação YAML: adicionada etapa `actionlint` no CI.
- JSON: validação alterada para `utf-8-sig`, aceitando BOM sem mascarar JSON inválido.
- Migrations: o CI passa a aplicar `database/apply_all_required_migrations.sql` duas vezes em PostgreSQL limpo.
- Gate local: `dotnet` não está instalado no container; restore/build/test não puderam ser executados localmente.
- Correções realizadas: contratos CMS deixaram de ser vazios, migration end-to-end adicionada, CI passou a executar migrations reais, policies foram movidas para ações do controller.
