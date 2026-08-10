-- Evolução Premium de Etiquetas e Classificação Arquivística.
-- Exclusivamente aditiva, idempotente e compatível com estruturas legadas.
create schema if not exists ged;

alter table if exists ged.classification_plan add column if not exists activity_type varchar(10) not null default 'MEIO';
alter table if exists ged.classification_plan add column if not exists display_order integer not null default 0;
alter table if exists ged.classification_plan add column if not exists current_retention_text text not null default '';
alter table if exists ged.classification_plan add column if not exists current_start_event text not null default '';
alter table if exists ged.classification_plan add column if not exists intermediate_retention_text text not null default '';
alter table if exists ged.classification_plan add column if not exists intermediate_start_event text not null default '';
alter table if exists ged.classification_plan add column if not exists normative_source text;
alter table if exists ged.classification_plan add column if not exists condition_exception text;
alter table if exists ged.classification_plan add column if not exists confidentiality_level varchar(30) not null default 'PUBLICO';
alter table if exists ged.classification_plan add column if not exists review_status varchar(30) not null default 'RASCUNHO';

alter table if exists ged.classification_plan_version_item add column if not exists activity_type varchar(10);
alter table if exists ged.classification_plan_version_item add column if not exists display_order integer;
alter table if exists ged.classification_plan_version_item add column if not exists current_retention_text text;
alter table if exists ged.classification_plan_version_item add column if not exists current_start_event text;
alter table if exists ged.classification_plan_version_item add column if not exists intermediate_retention_text text;
alter table if exists ged.classification_plan_version_item add column if not exists intermediate_start_event text;
alter table if exists ged.classification_plan_version_item add column if not exists normative_source text;
alter table if exists ged.classification_plan_version_item add column if not exists condition_exception text;
alter table if exists ged.classification_plan_version_item add column if not exists confidentiality_level varchar(30);
alter table if exists ged.classification_plan_version_item add column if not exists review_status varchar(30);

alter table if exists ged.document_classification add column if not exists classification_version_id uuid;
alter table if exists ged.document_classification add column if not exists classification_plan_id uuid;
alter table if exists ged.document_classification add column if not exists confidence numeric(5,4);
alter table if exists ged.document_classification add column if not exists suggestion_factors jsonb not null default '{}'::jsonb;
alter table if exists ged.document_classification add column if not exists reclassification_reason text;
alter table if exists ged.document_classification add column if not exists classified_by uuid;
alter table if exists ged.document_classification add column if not exists classified_at timestamptz not null default now();

create table if not exists ged.document_classification_audit (
 id bigserial primary key, tenant_id uuid not null, document_id uuid not null,
 previous_classification_id uuid, new_classification_id uuid not null,
 previous_version_id uuid, new_version_id uuid, reason text,
 impact_json jsonb not null default '{}'::jsonb, changed_by uuid not null,
 created_at timestamptz not null default now()
);
alter table ged.document_classification_audit add column if not exists created_at timestamptz default now();

create table if not exists ged.label_print_history (
 id uuid primary key, tenant_id uuid not null, label_subject_type varchar(20) not null,
 label_subject_id uuid not null, template_code varchar(60) not null,
 snapshot_json jsonb not null, snapshot_sha256 char(64) not null,
 printed_by uuid not null, printed_at timestamptz not null default now(),
 ip_address inet, user_agent text, reprint_reason text
);

alter table if exists ged.label_print add column if not exists snapshot_json jsonb;
alter table if exists ged.label_print add column if not exists snapshot_sha256 char(64);
alter table if exists ged.label_print add column if not exists template_version varchar(60);
alter table if exists ged.label_print add column if not exists ip_address inet;
alter table if exists ged.label_print add column if not exists user_agent text;
alter table if exists ged.label_print add column if not exists reprint_reason text;

create index if not exists ix_classification_plan_tenant_parent_order on ged.classification_plan(tenant_id,parent_id,display_order,code);
create index if not exists ix_classification_plan_tenant_status on ged.classification_plan(tenant_id,is_active,review_status);
create index if not exists ix_document_classification_audit_subject on ged.document_classification_audit(tenant_id,document_id,created_at desc);
create index if not exists ix_label_print_history_subject on ged.label_print_history(tenant_id,label_subject_type,label_subject_id,printed_at desc);
create unique index if not exists ux_label_print_history_hash on ged.label_print_history(tenant_id,id,snapshot_sha256);

-- Integridade multi-tenant: a classe vinculada deve existir no mesmo tenant.
create unique index if not exists ux_classification_plan_tenant_id on ged.classification_plan(tenant_id,id);
do $$ begin
 if to_regclass('ged.document_classification') is not null and not exists
   (select 1 from pg_constraint where conname='fk_document_classification_tenant_class') then
  alter table ged.document_classification add constraint fk_document_classification_tenant_class
   foreign key (tenant_id,classification_plan_id) references ged.classification_plan(tenant_id,id) not valid;
 end if;
end $$;
