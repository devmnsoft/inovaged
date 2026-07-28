# Runbook — Backup e Restore

- O backup PostgreSQL usa `pg_dump --format=custom`, `PGPASSFILE` temporário, arquivo `.partial`, validação com `pg_restore --list`, SHA-256, `manifest.json` e `checksums.sha256`.
- Nunca restaure sobre produção. Restore de homologação exige banco allowlistado e justificativa.
- Verifique tamanho, checksum, manifesto e legibilidade do dump antes de declarar o backup válido.
