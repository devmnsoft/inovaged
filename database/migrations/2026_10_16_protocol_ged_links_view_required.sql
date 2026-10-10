-- Correção incremental: a migration 2026_10_15 pode ter sido marcada como aplicada
-- mesmo quando apenas emitiu NOTICE e não criou a view. Este script não altera
-- o arquivo anterior. Dependência obrigatória ausente falha a transação.

DO $mig$
BEGIN
    IF to_regclass('ged.protocolo_documento_ged') IS NULL
       OR to_regclass('ged.protocolo') IS NULL
       OR to_regclass('ged.protocolo_documento') IS NULL THEN
        RAISE EXCEPTION 'Dependências obrigatórias ausentes para ged.vw_protocolo_ged_vinculos';
    END IF;
END $mig$;

CREATE OR REPLACE VIEW ged.vw_protocolo_ged_vinculos AS
 SELECT v.tenant_id,
    v.id,
    v.protocolo_id,
    p.numero AS protocolo_numero,
    v.protocolo_documento_id,
    pd.nome_arquivo AS protocolo_anexo_nome,
    v.ged_document_id,
    v.tipo_vinculo,
    v.observacao,
    v.criado_por_nome,
    v.created_at
   FROM ged.protocolo_documento_ged v
   LEFT JOIN ged.protocolo p ON p.id = v.protocolo_id AND p.tenant_id = v.tenant_id
   LEFT JOIN ged.protocolo_documento pd ON pd.id = v.protocolo_documento_id AND pd.tenant_id = v.tenant_id
  WHERE v.reg_status = 'A';

DO $mig$
BEGIN
    IF to_regclass('ged.vw_protocolo_ged_vinculos') IS NULL THEN
        RAISE EXCEPTION 'A view ged.vw_protocolo_ged_vinculos não ficou disponível após a criação';
    END IF;
END $mig$;
