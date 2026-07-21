# Runbook Operations Worker

- O worker permanece desabilitado por padrão via `Operations:WorkerEnabled=false`.
- Para homologação, configurar connection string, habilitar o worker e registrar `workerId` único por instância.
- O claim usa transação e `FOR UPDATE SKIP LOCKED`, preenchendo `worker_id`, `locked_until_utc`, `current_step` e `progress_percent`.
- Em incidentes, pausar o worker, analisar `ged.backup_job` e mover manualmente para `RETRY` apenas jobs idempotentes.
