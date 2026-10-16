-- Corrige a incompatibilidade character varying = ged.preview_processing_status (SQLSTATE 42883).
-- O texto recebido pela aplicação continua texto. A coluna ged.preview_status.status permanece o enum.
-- Não apaga linhas, tentativas, caminhos ou mensagens.
--
-- Recuperação, somente com backup conferido e janela de manutenção:
--   ALTER TABLE ged.preview_status ALTER COLUMN status TYPE text USING status::text;
-- Não remova ged.preview_processing_status enquanto a coluna depender dele.
-- Não há reversão automática. CANCELLED legado, quando ainda estiver em texto, passa a CANCELED.
-- ERROR permanece ERROR. FAILED permanece FAILED.

CREATE SCHEMA IF NOT EXISTS ged;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_type t
        JOIN pg_namespace n ON n.oid = t.typnamespace
        WHERE n.nspname = 'ged' AND t.typname = 'preview_processing_status'
    ) THEN
        CREATE TYPE ged.preview_processing_status AS ENUM ('PENDING','PROCESSING','READY','FAILED','CANCELED','ERROR');
    END IF;
END $$;

DO $$
DECLARE
    missing text;
    col_type text;
    col_udt text;
    unknown_count integer;
    null_count integer;
    duplicate_count integer;
BEGIN
    IF to_regclass('ged.preview_status') IS NULL THEN
        RAISE EXCEPTION 'ged.preview_status não existe. Aplique 2026_10_14_preview_status_upsert_contract antes. Nenhum dado foi alterado.'
            USING ERRCODE = 'P0001';
    END IF;

    SELECT string_agg(required.label, ', ' ORDER BY required.label)
      INTO missing
    FROM (SELECT unnest(ARRAY['PENDING','PROCESSING','READY','FAILED','CANCELED','ERROR']) AS label) required
    WHERE NOT EXISTS (
        SELECT 1
        FROM pg_enum e
        JOIN pg_type t ON t.oid = e.enumtypid
        JOIN pg_namespace n ON n.oid = t.typnamespace
        WHERE n.nspname = 'ged'
          AND t.typname = 'preview_processing_status'
          AND e.enumlabel = required.label
    );

    IF missing IS NOT NULL THEN
        RAISE EXCEPTION 'ged.preview_processing_status sem os rótulos: %. Aplique 2026_10_23_preview_status_requested_at_contract e execute esta migration outra vez. Nenhum dado foi alterado.', missing
            USING ERRCODE = 'P0001';
    END IF;

    SELECT c.data_type, c.udt_name
      INTO col_type, col_udt
    FROM information_schema.columns c
    WHERE c.table_schema = 'ged'
      AND c.table_name = 'preview_status'
      AND c.column_name = 'status';

    IF col_udt IS NULL THEN
        RAISE EXCEPTION 'ged.preview_status.status não existe. Nenhum dado foi alterado.'
            USING ERRCODE = 'P0001';
    END IF;

    SELECT count(*) INTO duplicate_count
    FROM (
        SELECT tenant_id, document_version_id
        FROM ged.preview_status
        GROUP BY tenant_id, document_version_id
        HAVING count(*) > 1
    ) duplicates;

    IF duplicate_count > 0 THEN
        RAISE EXCEPTION 'ged.preview_status possui % chaves tenant/version duplicadas. Nenhum dado foi alterado.', duplicate_count
            USING ERRCODE = 'P0001';
    END IF;

    IF col_udt IS DISTINCT FROM 'preview_processing_status' THEN
        IF col_type NOT IN ('character varying', 'text') THEN
            RAISE EXCEPTION 'ged.preview_status.status está como % (%). A conversão automática só cobre text e varchar. Nenhum dado foi alterado.', col_type, col_udt
                USING ERRCODE = 'P0001';
        END IF;

        SELECT count(*) INTO null_count
        FROM ged.preview_status
        WHERE status IS NULL;

        IF null_count > 0 THEN
            RAISE EXCEPTION 'ged.preview_status possui % status nulos. Corrija antes da conversão. Nenhum dado foi alterado.', null_count
                USING ERRCODE = 'P0001';
        END IF;

        SELECT count(*) INTO unknown_count
        FROM ged.preview_status
        WHERE upper(btrim(status::text)) NOT IN ('PENDING','PROCESSING','READY','FAILED','ERROR','CANCELED','CANCELLED');

        IF unknown_count > 0 THEN
            RAISE EXCEPTION 'ged.preview_status possui % status fora do enum. Nenhum dado foi alterado.', unknown_count
                USING ERRCODE = 'P0001';
        END IF;

        ALTER TABLE ged.preview_status ALTER COLUMN status DROP DEFAULT;
        ALTER TABLE ged.preview_status
            ALTER COLUMN status TYPE ged.preview_processing_status
            USING (
                CASE upper(btrim(status::text))
                    WHEN 'READY' THEN 'READY'
                    WHEN 'PROCESSING' THEN 'PROCESSING'
                    WHEN 'FAILED' THEN 'FAILED'
                    WHEN 'ERROR' THEN 'ERROR'
                    WHEN 'CANCELED' THEN 'CANCELED'
                    WHEN 'CANCELLED' THEN 'CANCELED'
                    ELSE 'PENDING'
                END::ged.preview_processing_status
            );
    END IF;
END $$;

ALTER TABLE ged.preview_status
    ALTER COLUMN status SET DEFAULT 'PENDING'::ged.preview_processing_status;

ALTER TABLE ged.preview_status
    ALTER COLUMN status SET NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_preview_status_tenant_version
    ON ged.preview_status (tenant_id, document_version_id);
