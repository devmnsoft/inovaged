# Runbook Backup e Restore

- Nunca executar restore sobre produção pela interface.
- `pg_dump` usa `PGPASSFILE` temporário com permissão restrita e senha fora da linha de comando.
- Artefatos parciais usam sufixo `.partial` e são removidos em falha.
- Restore test exige confirmação textual, justificativa e destino em allowlist `RestoreTest:AllowedDatabases`.
- Comparação de produção usa `NpgsqlConnectionStringBuilder` para host e database exatos.
