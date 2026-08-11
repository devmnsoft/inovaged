-- Busca inteligente: pg_trgm é opcional e pode estar instalado fora do search_path.
-- Esta migration é deliberadamente idempotente e preserva o fallback FTS/ILIKE.
do $$
declare
    v_trgm_schema text;
begin
    begin
        create extension if not exists pg_trgm;
    exception
        when insufficient_privilege then
            raise notice 'Extensão pg_trgm não pôde ser criada por falta de permissão. Busca usará FTS/ILIKE.';
        when undefined_file then
            raise notice 'Extensão pg_trgm não está disponível neste PostgreSQL. Busca usará FTS/ILIKE.';
        when others then
            raise notice 'Não foi possível criar pg_trgm: %. Busca usará FTS/ILIKE.', sqlerrm;
    end;

    select n.nspname
      into v_trgm_schema
      from pg_opclass oc
      join pg_am am on am.oid = oc.opcmethod
      join pg_namespace n on n.oid = oc.opcnamespace
     where am.amname = 'gin'
       and oc.opcname = 'gin_trgm_ops'
     order by (n.nspname = any (current_schemas(true))) desc, n.nspname
     limit 1;

    if to_regclass('ged.document_search_index') is null then
        raise notice 'ged.document_search_index ainda não existe; índices de busca não serão criados.';
        return;
    end if;

    if exists (
        select 1 from pg_attribute
         where attrelid = 'ged.document_search_index'::regclass
           and attname = 'search_vector' and not attisdropped
    ) then
        create index if not exists ix_document_search_index_vector
            on ged.document_search_index using gin(search_vector);
    end if;

    if v_trgm_schema is not null and exists (
        select 1 from pg_attribute
         where attrelid = 'ged.document_search_index'::regclass
           and attname = 'search_text' and not attisdropped
    ) then
        execute format(
            'create index if not exists ix_document_search_index_text_trgm on ged.document_search_index using gin (search_text %I.gin_trgm_ops)',
            v_trgm_schema
        );
    else
        raise notice 'gin_trgm_ops indisponível. Mantendo busca por FTS/ILIKE.';
    end if;
end $$;
