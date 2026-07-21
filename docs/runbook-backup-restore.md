# Runbook de Backup e Restore

1. Aplique `database/migrations/2026_07_backup_continuity_portability.sql`.
2. Configure `Backup:Enabled`, `Backup:RootPath` e `Backup:PostgresBinPath` fora do Git.
3. Solicite backup em `/Continuity/Backups`; o worker processa jobs persistidos.
4. Verifique integridade antes de considerar o conjunto utilizável.
5. Teste restore apenas em banco temporário na allowlist `RestoreTest:AllowedDatabases`.

Nunca restaure sobre produção, nunca publique dumps em `wwwroot`, nunca registre strings de conexão completas e nunca apague storage de produção como parte deste módulo.
