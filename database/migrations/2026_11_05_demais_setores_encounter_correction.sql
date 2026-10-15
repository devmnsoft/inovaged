-- Incremental migration: Enforce encounter/patient/document tracking on link trigger,
-- granular item tracking in clinical extraction, and persisted delivery lifecycle.

-- 1. Atualizar o trigger sector_processing_link para acompanhar mudanças de encounter_id, patient_id e document_id
drop trigger if exists sector_processing_link on ged.sector_document_link;
create trigger sector_processing_link after insert or update of version_id,catalog_code,reg_status,encounter_id,patient_id,document_id on ged.sector_document_link
 for each row execute function ged.sector_processing_notification();

-- 2. Colunas de contagem e segmentação na extração clínica
alter table ged.sector_clinical_extraction add column if not exists items_returned integer not null default 0;
alter table ged.sector_clinical_extraction add column if not exists items_recorded integer not null default 0;
alter table ged.sector_clinical_extraction add column if not exists items_preserved integer not null default 0;
alter table ged.sector_clinical_extraction add column if not exists total_segments integer not null default 1;
alter table ged.sector_clinical_extraction add column if not exists completed_segments integer not null default 1;

-- Atualizar constraint de status da extração clínica para incluir CONCLUIDO_PARCIAL
alter table ged.sector_clinical_extraction drop constraint if exists sector_clinical_extraction_status_check;
alter table ged.sector_clinical_extraction add constraint sector_clinical_extraction_status_check
 check(status in ('PROCESSANDO','AGUARDANDO_RESULTADO','CONCLUIDO','CONCLUIDO_PARCIAL','FALHOU','DESATUALIZADO','NEGADO'));

-- 3. Rastreamento ampliado de paciente e documento na correção de vínculo
alter table ged.sector_link_correction add column if not exists from_patient_id uuid;
alter table ged.sector_link_correction add column if not exists to_patient_id uuid;
alter table ged.sector_link_correction add column if not exists from_document_id uuid;
alter table ged.sector_link_correction add column if not exists to_document_id uuid;

-- 4. Preparação e entrega persistida com protocolo, confirmação de entrega e rastreabilidade de pendências
alter table ged.sector_delivery add column if not exists delivered_at timestamptz;
alter table ged.sector_delivery add column if not exists delivery_evidence text;
alter table ged.sector_delivery add column if not exists protocol_number text;
alter table ged.sector_delivery add column if not exists version_no integer not null default 1;

alter table ged.sector_delivery_item add column if not exists item_order integer not null default 0;
alter table ged.sector_delivery_item add column if not exists catalog_code text;
alter table ged.sector_delivery_item add column if not exists is_pending boolean not null default false;

create index if not exists ix_sector_delivery_encounter on ged.sector_delivery (tenant_id, encounter_id, created_at desc);
