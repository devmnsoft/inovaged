-- Permite registrar movimentação para a raiz, onde ged.document.folder_id é nulo.
-- Não apaga histórico. A coluna new_folder_id deixa de ser obrigatória.
-- correlation_id guarda o identificador da requisição quando a aplicação o envia.
-- O índice único evita duas linhas do mesmo lote para o mesmo documento.

ALTER TABLE ged.document_folder_move_history
    ALTER COLUMN new_folder_id DROP NOT NULL;

ALTER TABLE ged.document_folder_move_history
    ADD COLUMN IF NOT EXISTS correlation_id text NULL;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM pg_indexes
        WHERE schemaname = 'ged'
          AND indexname = 'ux_document_folder_move_history_tenant_batch_document'
    ) THEN
        RETURN;
    END IF;

    IF EXISTS (
        SELECT 1
        FROM ged.document_folder_move_history
        WHERE batch_id IS NOT NULL
          AND reg_status = 'A'
        GROUP BY tenant_id, batch_id, document_id
        HAVING count(*) > 1
    ) THEN
        RAISE NOTICE 'Índice único de lote não criado: já existem movimentações duplicadas.';
        RETURN;
    END IF;

    CREATE UNIQUE INDEX ux_document_folder_move_history_tenant_batch_document
        ON ged.document_folder_move_history (tenant_id, batch_id, document_id)
        WHERE batch_id IS NOT NULL AND reg_status = 'A';
END $$;
