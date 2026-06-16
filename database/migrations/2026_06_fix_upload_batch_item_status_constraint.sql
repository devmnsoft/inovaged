create schema if not exists ged;

alter table if exists ged.upload_batch_item
add column if not exists status text not null default 'PENDING';

alter table if exists ged.upload_batch_item
add column if not exists processing_warning text null;

-- Normalizar valores nulos/vazios antes da constraint
update ged.upload_batch_item
set status = 'PENDING'
where status is null or trim(status) = '';

-- Corrigir status legados incompatíveis, se existirem
update ged.upload_batch_item
set status = 'CANCELLED'
where upper(status) = 'CANCELED';

update ged.upload_batch_item
set status = 'QUEUED'
where upper(status) in ('OCR_QUEUED', 'PREVIEW_QUEUED', 'SMART_INDEX_QUEUED');

update ged.upload_batch_item
set status = 'ERROR'
where upper(status) in ('FAILED', 'FAILURE');

update ged.upload_batch_item
set status = 'COMPLETED'
where upper(status) in ('DONE', 'SUCCESS');

update ged.upload_batch_item
set status = upper(status)
where status <> upper(status);

update ged.upload_batch_item
set status = 'PENDING', processing_warning = concat_ws(' | ', processing_warning, 'Status legado incompatível normalizado para PENDING: ' || status)
where status not in (
    'PENDING',
    'RECEIVING',
    'SAVED',
    'DOCUMENT_CREATED',
    'QUEUED',
    'COMPLETED',
    'ERROR',
    'SKIPPED',
    'ABORTED',
    'RETRYABLE',
    'DUPLICATE',
    'CANCELLED'
);

-- Remover constraint antiga, se existir
do $$
begin
    if exists (
        select 1
        from pg_constraint
        where conname = 'ck_upload_batch_item_status'
          and conrelid = 'ged.upload_batch_item'::regclass
    ) then
        alter table ged.upload_batch_item
        drop constraint ck_upload_batch_item_status;
    end if;
end $$;

-- Recriar constraint com todos os status usados pelo código
alter table ged.upload_batch_item
add constraint ck_upload_batch_item_status
check (
    status in (
        'PENDING',
        'RECEIVING',
        'SAVED',
        'DOCUMENT_CREATED',
        'QUEUED',
        'COMPLETED',
        'ERROR',
        'SKIPPED',
        'ABORTED',
        'RETRYABLE',
        'DUPLICATE',
        'CANCELLED'
    )
);

create index if not exists ix_upload_batch_item_tenant_batch_status
on ged.upload_batch_item(tenant_id, batch_id, status);

create index if not exists ix_upload_batch_item_retryable
on ged.upload_batch_item(tenant_id, batch_id, status, can_retry)
where status in ('ERROR', 'ABORTED', 'RETRYABLE');
