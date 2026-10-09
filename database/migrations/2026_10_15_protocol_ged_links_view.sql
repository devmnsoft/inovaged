-- Garante a view de vínculos GED/protocolo em upgrade sem recriar tabelas.
-- Instalação limpa que já aplica database/base também permanece compatível:
-- CREATE OR REPLACE não apaga linhas de protocolo_documento_ged.

DO $mig$
BEGIN
    IF to_regclass('ged.protocolo_documento_ged') IS NULL
       OR to_regclass('ged.protocolo') IS NULL
       OR to_regclass('ged.protocolo_documento') IS NULL THEN
        RAISE NOTICE 'Dependências de protocolo ausentes; vw_protocolo_ged_vinculos não recriada.';
        RETURN;
    END IF;

    EXECUTE $view$
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
  WHERE v.reg_status = 'A'
$view$;
END $mig$;
