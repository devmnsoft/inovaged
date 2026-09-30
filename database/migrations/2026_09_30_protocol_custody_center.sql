-- Central de tramitação: custódia identificada, idempotência e execução de temporalidade.
-- Aditiva e reaplicável. Não altera numeração, histórico aplicado nem une ged.protocolo com ged.protocol_request.
-- Cada bloco só corre se a tabela de origem já existir (gedscript ou migration anterior).

CREATE SCHEMA IF NOT EXISTS ged;

DO $$
BEGIN
    IF to_regclass('ged.protocolo') IS NULL OR to_regclass('ged.protocolo_tramitacao') IS NULL THEN
        RAISE NOTICE 'ged.protocolo ausente; bloco institucional adiado até o schema base.';
        RETURN;
    END IF;

    EXECUTE 'ALTER TABLE ged.protocolo ADD COLUMN IF NOT EXISTS situacao_custodia varchar(40)';
    EXECUTE 'ALTER TABLE ged.protocolo_tramitacao ADD COLUMN IF NOT EXISTS situacao_movimentacao varchar(40)';
    EXECUTE 'ALTER TABLE ged.protocolo_tramitacao ADD COLUMN IF NOT EXISTS protocolo_documento_id uuid';
    EXECUTE 'ALTER TABLE ged.protocolo_tramitacao ADD COLUMN IF NOT EXISTS idempotency_key text';
    EXECUTE 'ALTER TABLE ged.protocolo_tramitacao ADD COLUMN IF NOT EXISTS correlation_id text';
    EXECUTE 'ALTER TABLE ged.protocolo_tramitacao ADD COLUMN IF NOT EXISTS recebida_em timestamp without time zone';
    EXECUTE 'ALTER TABLE ged.protocolo_tramitacao ADD COLUMN IF NOT EXISTS recebida_por uuid';
    EXECUTE 'ALTER TABLE ged.protocolo_tramitacao ADD COLUMN IF NOT EXISTS recebida_por_nome varchar(200)';
    EXECUTE 'ALTER TABLE ged.protocolo_tramitacao ADD COLUMN IF NOT EXISTS estorno_de_id uuid';
    EXECUTE 'ALTER TABLE ged.protocolo_tramitacao ADD COLUMN IF NOT EXISTS ativa boolean NOT NULL DEFAULT false';
    EXECUTE 'ALTER TABLE ged.protocolo_tramitacao ADD COLUMN IF NOT EXISTS prazo_em timestamp without time zone';
    EXECUTE 'ALTER TABLE ged.protocolo_tramitacao ADD COLUMN IF NOT EXISTS entregue_a varchar(200)';
    EXECUTE 'ALTER TABLE ged.protocolo_tramitacao ADD COLUMN IF NOT EXISTS responsavel_destino_id uuid';

    EXECUTE $upd$
        UPDATE ged.protocolo_tramitacao
        SET situacao_movimentacao = 'RECEBIDA', ativa = false
        WHERE situacao_movimentacao IS NULL AND acao = 'TRAMITACAO' AND reg_status = 'A'
    $upd$;
    EXECUTE $upd$
        UPDATE ged.protocolo
        SET situacao_custodia = CASE
            WHEN upper(status) IN ('FINALIZADO','ARQUIVADO','CANCELADO','DEFERIDO','INDEFERIDO') THEN 'ENCERRADO'
            ELSE 'EM_CUSTODIA' END
        WHERE situacao_custodia IS NULL
    $upd$;

    EXECUTE 'CREATE UNIQUE INDEX IF NOT EXISTS ux_protocolo_custodia_ativa ON ged.protocolo_tramitacao (tenant_id, protocolo_id, COALESCE(protocolo_documento_id, ''00000000-0000-0000-0000-000000000000''::uuid)) WHERE reg_status = ''A'' AND ativa = true AND situacao_movimentacao IN (''AGUARDANDO_RECEBIMENTO'',''DEVOLUCAO_PENDENTE'')';
    EXECUTE 'CREATE UNIQUE INDEX IF NOT EXISTS ux_protocolo_tramitacao_idempotency ON ged.protocolo_tramitacao (tenant_id, idempotency_key) WHERE idempotency_key IS NOT NULL AND reg_status = ''A''';
    EXECUTE 'CREATE INDEX IF NOT EXISTS ix_protocolo_fila_custodia ON ged.protocolo (tenant_id, situacao_custodia, setor_atual_id, created_at DESC) WHERE reg_status = ''A''';
    EXECUTE 'CREATE INDEX IF NOT EXISTS ix_protocolo_tramitacao_destino ON ged.protocolo_tramitacao (tenant_id, setor_destino_id, situacao_movimentacao, data_tramitacao DESC) WHERE reg_status = ''A''';
END $$;

DO $$
BEGIN
    IF to_regclass('ged.protocol_tramitation') IS NULL THEN
        RAISE NOTICE 'ged.protocol_tramitation ausente; bloco de solicitação adiado.';
        RETURN;
    END IF;
    EXECUTE 'ALTER TABLE ged.protocol_tramitation ADD COLUMN IF NOT EXISTS item_id uuid';
    EXECUTE 'ALTER TABLE ged.protocol_tramitation ADD COLUMN IF NOT EXISTS idempotency_key text';
    EXECUTE 'ALTER TABLE ged.protocol_tramitation ADD COLUMN IF NOT EXISTS correlation_id text';
    EXECUTE 'ALTER TABLE ged.protocol_tramitation ADD COLUMN IF NOT EXISTS reversed_from_id uuid';
    EXECUTE 'ALTER TABLE ged.protocol_tramitation ADD COLUMN IF NOT EXISTS reversal_reason text';
    EXECUTE 'ALTER TABLE ged.protocol_tramitation DROP CONSTRAINT IF EXISTS ck_protocol_tramitation_status';
    EXECUTE 'ALTER TABLE ged.protocol_tramitation ADD CONSTRAINT ck_protocol_tramitation_status CHECK (status IN (''PENDING_RECEIPT'',''RECEIVED'',''RETURNED'',''RETURN_PENDING'',''RETURN_CONFIRMED'',''COMPLETED'',''CANCELLED'',''REVERSED''))';
    EXECUTE 'CREATE UNIQUE INDEX IF NOT EXISTS ux_protocol_tramitation_active_unit ON ged.protocol_tramitation (tenant_id, protocol_request_id, COALESCE(item_id, ''00000000-0000-0000-0000-000000000000''::uuid)) WHERE reg_status = ''A'' AND status IN (''PENDING_RECEIPT'',''RETURN_PENDING'')';
    EXECUTE 'CREATE UNIQUE INDEX IF NOT EXISTS ux_protocol_tramitation_idempotency ON ged.protocol_tramitation (tenant_id, idempotency_key) WHERE idempotency_key IS NOT NULL AND reg_status = ''A''';
    EXECUTE 'CREATE INDEX IF NOT EXISTS ix_protocol_tramitation_destination_open ON ged.protocol_tramitation (tenant_id, destination_sector_id, status, forwarded_at DESC) WHERE reg_status = ''A''';
END $$;

DO $$
BEGIN
    IF to_regclass('ged.protocol_request_attachment') IS NULL THEN
        RAISE NOTICE 'ged.protocol_request_attachment ausente; pendência de anexo adiada.';
        RETURN;
    END IF;
    EXECUTE 'ALTER TABLE ged.protocol_request_attachment ADD COLUMN IF NOT EXISTS storage_state text NOT NULL DEFAULT ''STORED''';
    EXECUTE 'ALTER TABLE ged.protocol_request_attachment ADD COLUMN IF NOT EXISTS failure_stage text';
    EXECUTE 'ALTER TABLE ged.protocol_request_attachment ADD COLUMN IF NOT EXISTS failure_reason text';
    EXECUTE 'ALTER TABLE ged.protocol_request_attachment ADD COLUMN IF NOT EXISTS correlation_id text';
    EXECUTE 'ALTER TABLE ged.protocol_request_attachment ADD COLUMN IF NOT EXISTS attempt_count integer NOT NULL DEFAULT 1';
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_protocol_attachment_storage_state'
          AND conrelid = 'ged.protocol_request_attachment'::regclass
    ) THEN
        EXECUTE 'ALTER TABLE ged.protocol_request_attachment ADD CONSTRAINT ck_protocol_attachment_storage_state CHECK (storage_state IN (''PENDING'',''STORED'',''FAILED''))';
    END IF;
    EXECUTE 'CREATE INDEX IF NOT EXISTS ix_protocol_attachment_state ON ged.protocol_request_attachment (tenant_id, protocol_request_id, storage_state) WHERE reg_status = ''A''';
END $$;

DO $$
BEGIN
    IF to_regclass('ged.retention_case') IS NULL OR to_regclass('ged.retention_case_item') IS NULL THEN
        RAISE NOTICE 'ged.retention_case ausente; resultado de execução adiado.';
        RETURN;
    END IF;
    EXECUTE 'ALTER TABLE ged.retention_case_item ADD COLUMN IF NOT EXISTS execution_status varchar(30)';
    EXECUTE 'ALTER TABLE ged.retention_case_item ADD COLUMN IF NOT EXISTS execution_block_reason text';
    EXECUTE 'ALTER TABLE ged.retention_case_item ADD COLUMN IF NOT EXISTS execution_block_source text';
    EXECUTE 'ALTER TABLE ged.retention_case ADD COLUMN IF NOT EXISTS execution_outcome varchar(30)';
END $$;

CREATE OR REPLACE FUNCTION ged.next_loan_protocol_no(p_tenant uuid)
RETURNS bigint
LANGUAGE plpgsql
AS $$
DECLARE
    n bigint;
BEGIN
    IF to_regclass('ged.loan_request') IS NULL THEN
        RAISE EXCEPTION 'ged.loan_request não existe';
    END IF;
    PERFORM pg_advisory_xact_lock(hashtext(p_tenant::text || ':loan_request'));
    SELECT coalesce(max(protocol_no), 0) + 1 INTO n
    FROM ged.loan_request
    WHERE tenant_id = p_tenant;
    RETURN n;
END $$;

DO $$
BEGIN
    IF to_regclass('ged.loan_request') IS NULL THEN
        RAISE NOTICE 'ged.loan_request ausente; índice de empréstimo adiado.';
        RETURN;
    END IF;
    IF EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = 'ged' AND indexname = 'ux_loan_request_open_protocol_request') THEN
        RETURN;
    END IF;
    IF EXISTS (
        SELECT 1
        FROM ged.loan_request
        WHERE reg_status = 'A'
          AND protocol_request_id IS NOT NULL
          AND status::text IN ('REQUESTED','APPROVED','DELIVERED','OVERDUE')
        GROUP BY tenant_id, protocol_request_id
        HAVING count(*) > 1
    ) THEN
        RAISE NOTICE 'Índice de empréstimo aberto não criado: há protocolos com mais de um empréstimo em aberto.';
        RETURN;
    END IF;
    CREATE UNIQUE INDEX ux_loan_request_open_protocol_request
    ON ged.loan_request (tenant_id, protocol_request_id)
    WHERE reg_status = 'A'
      AND protocol_request_id IS NOT NULL
      AND status::text IN ('REQUESTED','APPROVED','DELIVERED','OVERDUE');
END $$;
