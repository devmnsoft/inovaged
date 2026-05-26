-- Colunas da tabela principal de documentos
select table_schema, table_name, column_name, data_type, udt_name
from information_schema.columns
where table_schema = 'ged'
and table_name in ('document', 'documents', 'document_version', 'document_versions')
order by table_name, ordinal_position;

-- Colunas candidatas de pasta
select table_schema, table_name, column_name, data_type, udt_name
from information_schema.columns
where table_schema = 'ged'
and (
    column_name ilike '%folder%'
    or column_name ilike '%pasta%'
    or column_name ilike '%parent%'
)
order by table_name, ordinal_position;

-- Colunas de histórico de movimentação
select table_schema, table_name, column_name, data_type, udt_name
from information_schema.columns
where table_schema = 'ged'
and table_name = 'document_folder_move_history'
order by ordinal_position;

-- Colunas de pastas
select table_schema, table_name, column_name, data_type, udt_name
from information_schema.columns
where table_schema = 'ged'
and table_name in ('folder', 'folders')
order by table_name, ordinal_position;
