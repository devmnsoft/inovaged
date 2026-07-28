# Compatibilidade de banco CMS

A migration `2026_07_signature_cms_operational_fix.sql` é reconciliadora e idempotente. Ela preserva colunas legadas (`check_name`, `check_status`, `content_download_token_hash`, `idempotency_key`, `failed_attempts`) e cria colunas canônicas (`name`, `status`, `content_token_hash`, `completion_idempotency_key`, `failure_count`).

Não há `DROP TABLE` nem `DROP COLUMN`. Backfills usam `COALESCE` e índices são criados com `IF NOT EXISTS`.
