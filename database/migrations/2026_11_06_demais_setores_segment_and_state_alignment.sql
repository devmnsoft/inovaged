-- Incremental migration: Durable clinical extraction segments,
-- state alignment ('BLOQUEADO_POLITICA', 'REVISAO_NECESSARIA'),
-- and historically stable delivery composition snapshot.

-- 1. Alinhamento de status da extração clínica para incluir BLOQUEADO_POLITICA
alter table ged.sector_clinical_extraction drop constraint if exists sector_clinical_extraction_status_check;
alter table ged.sector_clinical_extraction add constraint sector_clinical_extraction_status_check
 check(status in ('PROCESSANDO','AGUARDANDO_RESULTADO','CONCLUIDO','CONCLUIDO_PARCIAL','FALHOU','DESATUALIZADO','NEGADO','BLOQUEADO_POLITICA'));

-- 2. Tabela de persistência durável por segmento de extração clínica
create table if not exists ged.sector_clinical_extraction_segment (
    id uuid primary key,
    tenant_id uuid not null,
    extraction_id uuid not null references ged.sector_clinical_extraction(id) on delete cascade,
    segment_index integer not null,
    char_start integer not null,
    char_end integer not null,
    page_start integer,
    page_end integer,
    input_hash char(64) not null,
    ai_execution_id uuid,
    status text not null check(status in ('PROCESSANDO','AGUARDANDO_RESULTADO','CONCLUIDO','FALHOU','BLOQUEADO_POLITICA','DESATUALIZADO','NEGADO')),
    items_returned integer not null default 0,
    items_recorded integer not null default 0,
    attempts integer not null default 1,
    error_message text,
    created_at timestamptz not null default now(),
    completed_at timestamptz,
    unique(tenant_id, extraction_id, segment_index)
);

create index if not exists ix_sector_clinical_seg_exec on ged.sector_clinical_extraction_segment(tenant_id, ai_execution_id) where ai_execution_id is not null;
create index if not exists ix_sector_clinical_seg_run on ged.sector_clinical_extraction_segment(tenant_id, extraction_id, segment_index);

-- 3. Associação de cada campo extraído ao seu respectivo segmento de origem
alter table ged.sector_extracted_field add column if not exists segment_id uuid;
create index if not exists ix_sector_extracted_field_segment on ged.sector_extracted_field(tenant_id, segment_id) where segment_id is not null;

-- 4. Composição historicamente estável na preparação e entrega documental
alter table ged.sector_delivery_item add column if not exists document_id uuid;
alter table ged.sector_delivery_item add column if not exists document_title text;
alter table ged.sector_delivery_item add column if not exists catalog_name text;
alter table ged.sector_delivery_item add column if not exists physical_required boolean not null default false;
alter table ged.sector_delivery_item add column if not exists physical_evidence text;
alter table ged.sector_delivery_item add column if not exists pending_reason text;

alter table ged.sector_delivery add column if not exists profile_version_no integer not null default 1;
alter table ged.sector_delivery add column if not exists profile_name text;
alter table ged.sector_delivery add column if not exists confirmed_by uuid;
