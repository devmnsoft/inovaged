-- Coleções privadas do SmartSearch: referências, nunca cópias de arquivos.
begin;
create table if not exists ged.smart_search_collection (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    user_id uuid not null,
    name varchar(120) not null,
    revision integer not null default 0,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    reg_status char(1) not null default 'A',
    constraint ck_smart_search_collection_name check (length(btrim(name)) between 1 and 120)
);

create table if not exists ged.smart_search_collection_item (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    collection_id uuid not null references ged.smart_search_collection(id),
    document_id uuid not null,
    version_id uuid null,
    reference_mode varchar(12) not null default 'CURRENT',
    added_by uuid not null,
    added_at timestamptz not null default now(),
    removed_at timestamptz null,
    reg_status char(1) not null default 'A',
    constraint ck_smart_search_collection_reference check (
      (reference_mode='CURRENT' and version_id is null) or (reference_mode='FIXED' and version_id is not null)
    ),
    constraint ux_smart_search_collection_item unique(tenant_id,collection_id,document_id)
);

create index if not exists ix_smart_search_collection_owner
  on ged.smart_search_collection(tenant_id,user_id,updated_at desc) where reg_status='A';
create index if not exists ix_smart_search_collection_item_active
  on ged.smart_search_collection_item(tenant_id,collection_id,added_at desc) where reg_status='A';
create index if not exists ix_smart_search_collection_item_document
  on ged.smart_search_collection_item(tenant_id,document_id) where reg_status='A';
commit;
