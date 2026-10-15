-- Explicit, governed clinical suggestions. No tenant policy is enabled here.
alter table ged.sector_extracted_field add column if not exists structured_value jsonb;
alter table ged.sector_extracted_field add column if not exists ai_execution_id uuid;
alter table ged.sector_extracted_field add column if not exists source_fingerprint char(64);
create table if not exists ged.sector_clinical_extraction (
 id uuid primary key,
 tenant_id uuid not null,
 user_id uuid not null,
 link_id uuid not null references ged.sector_document_link(id),
 version_id uuid not null,
 request_key text not null,
 input_hash char(64) not null,
 mechanism text not null,
 status text not null check(status in ('PROCESSANDO','AGUARDANDO_RESULTADO','CONCLUIDO','FALHOU','DESATUALIZADO','NEGADO')),
 execution_id uuid,
 reused_from uuid,
 item_count integer not null default 0,
 created_at timestamptz not null default now(),
 completed_at timestamptz,
 unique(tenant_id,user_id,request_key)
);
create index if not exists ix_sector_clinical_version on ged.sector_clinical_extraction(tenant_id,version_id,input_hash,created_at desc);
create index if not exists ix_sector_clinical_execution on ged.sector_extracted_field(tenant_id,ai_execution_id) where ai_execution_id is not null;
