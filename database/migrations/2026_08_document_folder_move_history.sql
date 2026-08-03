begin;

create schema if not exists ged;

create table if not exists ged.document_folder_move_history
(
    id uuid primary key,
    tenant_id uuid not null,
    document_id uuid not null,
    old_folder_id uuid null,
    new_folder_id uuid null,
    moved_by uuid not null,
    moved_by_name text null,
    moved_at timestamptz not null default now(),
    reason text null,
    batch_id uuid null,
    source varchar(32) not null default 'SINGLE',
    reg_status char(1) not null default 'A',
    constraint ck_document_folder_move_history_reg_status check (reg_status in ('A', 'I', 'E'))
);

create index if not exists ix_document_folder_move_history_tenant_document_date
    on ged.document_folder_move_history (tenant_id, document_id, moved_at desc);
create index if not exists ix_document_folder_move_history_tenant_batch
    on ged.document_folder_move_history (tenant_id, batch_id) where batch_id is not null;
create index if not exists ix_document_folder_move_history_tenant_destination
    on ged.document_folder_move_history (tenant_id, new_folder_id) where new_folder_id is not null;
create index if not exists ix_document_folder_move_history_moved_at
    on ged.document_folder_move_history (moved_at desc);

comment on table ged.document_folder_move_history is
    'Trilha imutavel e multi-tenant das movimentacoes de documentos entre pastas.';

commit;
