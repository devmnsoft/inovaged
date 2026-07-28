# Workflow canônico

O único workflow executável é `inovaged-ci.yml`. A cadeia é estritamente dependente: `actionlint`, `solution-validation`, servidores/Windows/segurança, migrations, CMS E2E, PoC e `release-gate`.

O job Linux gera HMAC efêmero mascarado, inicia ambos os hosts em loopback, consulta os endpoints live/ready com timeout, encerra os processos em uma etapa `always()` e publica logs somente em falha. O job Windows executa versionamento, diagnóstico, instalação isolada, rotação, desinstalação e publicação `win-x64`. O CMS E2E possui PostgreSQL 16 próprio.

Arquivos históricos permanecem em `docs/ci/archive` e não são interpretados pelo GitHub Actions nem pelo actionlint glob canônico.
