-- InovaGED - Parameters, SmartSearch, GED UI/navigation and BI support (idempotent, text-only)

create index if not exists ix_document_tenant_created
on ged.document(tenant_id, created_at desc);

create index if not exists ix_document_tenant_folder_created
on ged.document(tenant_id, folder_id, created_at desc);

create index if not exists ix_document_version_tenant_document
on ged.document_version(tenant_id, document_id);

do $$
begin
    if to_regclass('ged.upload_batch') is not null then
        create index if not exists ix_upload_batch_tenant_created_status
        on ged.upload_batch(tenant_id, created_at desc, status);
    end if;

    if to_regclass('ged.upload_batch_item') is not null then
        create index if not exists ix_upload_batch_item_tenant_status
        on ged.upload_batch_item(tenant_id, status);
    end if;

    if to_regclass('ged.search_query_log') is not null then
        create index if not exists ix_search_query_log_tenant_created
        on ged.search_query_log(tenant_id, created_at desc);
    end if;

    if to_regclass('ged.document_search_index') is not null then
        create index if not exists ix_document_search_index_tenant_document
        on ged.document_search_index(tenant_id, document_id);
        create index if not exists ix_document_search_index_tenant_folder
        on ged.document_search_index(tenant_id, folder_id);
    end if;
end $$;

create table if not exists ged.folder_virtual_map
(
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    virtual_folder_id uuid not null,
    real_folder_id uuid not null,
    created_at timestamptz not null default now(),
    created_by uuid null,
    reg_status char(1) not null default 'A'
);

create unique index if not exists ux_folder_virtual_map_active
on ged.folder_virtual_map(tenant_id, virtual_folder_id)
where reg_status='A';

create index if not exists ix_folder_virtual_map_real
on ged.folder_virtual_map(tenant_id, real_folder_id)
where reg_status='A';
