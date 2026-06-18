-- InovaGED - Upload em lote com possível duplicidade liberada mediante confirmação
-- Idempotente e textual.
CREATE SCHEMA IF NOT EXISTS ged;
DO $$
BEGIN
    CREATE EXTENSION IF NOT EXISTS pgcrypto;
EXCEPTION WHEN insufficient_privilege THEN
    RAISE NOTICE 'Sem permissão para criar pgcrypto. gen_random_uuid deve existir no ambiente.';
END $$;

alter table ged.upload_batch_item add column if not exists upload_client_id text null;
alter table ged.upload_batch_item add column if not exists content_hash text null;
alter table ged.upload_batch_item add column if not exists duplicate_of_document_id uuid null;
alter table ged.upload_batch_item add column if not exists duplicate_scope text null;
alter table ged.upload_batch_item add column if not exists duplicate_resolution text null;
alter table ged.upload_batch_item add column if not exists confirmed_duplicate_upload boolean not null default false;
alter table ged.upload_batch_item add column if not exists error_step text null;
alter table ged.upload_batch_item add column if not exists can_retry boolean not null default true;
alter table ged.upload_batch_item add column if not exists updated_at timestamptz null;

-- Índices únicos antigos transformavam retry/novo exame em bloqueio rígido.
drop index if exists ged.ux_upload_batch_item_file_idempotency;
drop index if exists ged.ux_upload_batch_item_dedup;

create index if not exists ix_upload_batch_item_tenant_batch_client
on ged.upload_batch_item(tenant_id, batch_id, upload_client_id);

create index if not exists ix_upload_batch_item_tenant_hash
on ged.upload_batch_item(tenant_id, content_hash);

create index if not exists ix_upload_batch_item_tenant_duplicate
on ged.upload_batch_item(tenant_id, duplicate_of_document_id);

create table if not exists ged.upload_duplicate_decision (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    batch_id uuid null,
    upload_batch_item_id uuid null,
    document_id uuid null,
    duplicate_of_document_id uuid null,
    file_name text not null,
    duplicate_scope text not null,
    selected_action text not null,
    confirmed_duplicate_upload boolean not null default false,
    reason text null,
    decided_by uuid null,
    decided_at timestamptz not null default now(),
    details_json jsonb null,
    reg_status char(1) not null default 'A'
);

create index if not exists ix_upload_duplicate_decision_tenant_batch
on ged.upload_duplicate_decision(tenant_id, batch_id, decided_at desc);

create index if not exists ix_upload_duplicate_decision_tenant_document
on ged.upload_duplicate_decision(tenant_id, document_id);
