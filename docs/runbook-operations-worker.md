# Runbook — Operations Worker

1. Configurar `ConnectionStrings:DefaultConnection`, `Backup:Enabled=true`, `Backup:RootPath` e caminho de binários PostgreSQL quando necessário.
2. Executar o worker com identidade sem privilégios administrativos locais.
3. Monitorar `ged.operations_worker_heartbeat`, `ged.backup_job` e `ged.operation_job_event`.
4. Jobs válidos usam estados `PENDING`, `CLAIMED`, `RUNNING`, `VERIFYING`, `COMPLETED`, `RETRY`, `FAILED`, `CANCEL_REQUESTED`, `CANCELLED` e `DEAD_LETTER`.
5. Em falha, conferir `current_step`, eventos e artefatos `.partial` antes de reprocessar.
