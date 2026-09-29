-- ============================================================================
-- 2026_09_29_ged_upload_chunk_compatibility.sql
-- Module: GedUploadChunk
-- ----------------------------------------------------------------------------
-- Compatibiliza a tabela ged.upload_session (criada pela migration consolidada
-- 2026_08_archival_schema_consolidation) com o fluxo de upload em chunks
-- (20260603_upload_chunk). Quando a tabela já existia antes da migration de
-- chunks, as colunas abaixo não foram criadas e o fluxo de chunks falhava com
-- erro amigável UPLOAD_CHUNK_SCHEMA_MISSING (400) ao iniciar a sessão.
--
-- Idempotente e aditivo (ADD COLUMN IF NOT EXISTS / CREATE INDEX IF NOT EXISTS).
-- Não altera dados existentes.
-- ============================================================================

ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS batch_item_id uuid NULL;
ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS metadata_json jsonb NULL;

CREATE INDEX IF NOT EXISTS ix_upload_session_batch_item
    ON ged.upload_session(tenant_id, batch_item_id)
    WHERE batch_item_id IS NOT NULL;
