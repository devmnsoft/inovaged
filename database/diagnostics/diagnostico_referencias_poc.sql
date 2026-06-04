-- Diagnóstico de referências operacionais a termos conceituais no banco InovaGED.
-- Execute antes do saneamento textual e salve o resultado como evidência de validação.
-- Nenhum comando deste arquivo altera dados.

with termos(term) as (
    values ('poc'), ('prova de conceito'), ('proof of concept'), ('demo'), ('demonstração'),
           ('sample'), ('mock'), ('fake'), ('fictício'), ('ficticio'), ('simulado'), ('lorem ipsum')
), alvos(schema_name, table_name, column_name) as (
    values
        ('ged','folder','name'),
        ('ged','folder','description'),
        ('ged','document','title'),
        ('ged','document','original_file_name'),
        ('ged','document','description'),
        ('ged','document_type','name'),
        ('ged','document_type','description'),
        ('ged','classification_rule','name'),
        ('ged','classification_rule','description'),
        ('ged','app_user','name'),
        ('ged','app_user','email'),
        ('ged','upload_batch','notes'),
        ('ged','audit_log','summary'),
        ('ged','system_log','message')
), existentes as (
    select a.*
    from alvos a
    join information_schema.columns c
      on c.table_schema = a.schema_name
     and c.table_name = a.table_name
     and c.column_name = a.column_name
)
select format(
    'select %L as tabela, %L as coluna, id::text as id, left(%I::text, 300) as valor from %I.%I where %I::text ilike any (array[%s]);',
    schema_name || '.' || table_name,
    column_name,
    column_name,
    schema_name,
    table_name,
    column_name,
    (select string_agg(quote_literal('%' || term || '%'), ',') from termos)
) as consulta_diagnostico
from existentes
order by schema_name, table_name, column_name;
