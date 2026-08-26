-- Labels Template Catalog Stability Fix.
-- Repõe os modelos de sistema sem sobrescrever personalizações existentes.
create schema if not exists ged;

do $$
begin
    if to_regclass('ged.label_template') is not null
       and exists (select 1 from information_schema.columns where table_schema='ged' and table_name='label_template' and column_name='code') then
        insert into ged.label_template
            (id, tenant_id, code, name, description, print_mode, subject_type, view_name,
             version, is_system_template, is_custom_template, is_active, is_default, reg_status)
        select gen_random_uuid(), null, seed.code, seed.name, seed.description, seed.print_mode,
               seed.subject_type, seed.view_name, 1, true, seed.is_custom, true, seed.is_default, 'A'
        from (values
            ('FACTORY_BOX_V1', 'Padrão do Sistema - Caixa', 'Etiqueta padrão do InovaGED para caixas físicas.', 'FACTORY', 'BOX', 'BoxLabel', false, true),
            ('FACTORY_DOCUMENT_V1', 'Padrão do Sistema - Documento/Pasta', 'Etiqueta padrão do InovaGED para documentos e pastas.', 'FACTORY', 'DOCUMENT', 'DocumentLabel', false, true),
            ('LOCDESK_CAIXA_V1', 'LocDesk - Caixa', 'Modelo personalizado LocDesk para identificação de caixas físicas.', 'CUSTOM', 'BOX', 'LocDeskBoxLabel', true, false),
            ('LOCDESK_PASTA_V1', 'LocDesk - Pasta', 'Modelo personalizado LocDesk para identificação de pastas/documentos.', 'CUSTOM', 'DOCUMENT', 'LocDeskFolderLabel', true, false)
        ) as seed(code, name, description, print_mode, subject_type, view_name, is_custom, is_default)
        where not exists (
            select 1 from ged.label_template current
            where upper(current.code::text) = upper(seed.code)
              and current.tenant_id is null
              and coalesce(current.reg_status, 'A') = 'A');
    end if;
end $$;
