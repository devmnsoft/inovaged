-- Consolidação funcional Guardião/eventos/dossiês. Idempotente e sem DROP.
create schema if not exists ged;
create extension if not exists pgcrypto;

-- Padrão canônico permanece sem sufixo guardian: document_twin, document_finding, document_finding_evidence, document_relationship.
-- Views de compatibilidade apenas para instalações/consultas antigas com nomes document_guardian_*.
do $$ begin
  if to_regclass('ged.document_twin') is not null and to_regclass('ged.document_guardian_twin') is null then execute 'create view ged.document_guardian_twin as select * from ged.document_twin'; end if;
  if to_regclass('ged.document_finding') is not null and to_regclass('ged.document_guardian_finding') is null then execute 'create view ged.document_guardian_finding as select * from ged.document_finding'; end if;
  if to_regclass('ged.document_finding_evidence') is not null and to_regclass('ged.document_guardian_evidence') is null then execute 'create view ged.document_guardian_evidence as select * from ged.document_finding_evidence'; end if;
  if to_regclass('ged.document_relationship') is not null and to_regclass('ged.document_guardian_relationship') is null then execute 'create view ged.document_guardian_relationship as select * from ged.document_relationship'; end if;
end $$;

create table if not exists ged.internal_outbox_event (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, aggregate_type text not null, aggregate_id uuid null,
 event_type text not null, payload jsonb not null default '{}'::jsonb, status text not null default 'PENDING', attempts int not null default 0,
 max_attempts int not null default 5, scheduled_at_utc timestamptz not null default now(), processed_at_utc timestamptz null,
 last_error text null, correlation_id text not null, occurred_at_utc timestamptz not null default now(), created_at_utc timestamptz not null default now()
);

create table if not exists ged.document_guardian_evaluation_queue (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, document_id uuid not null, reason text not null, priority int not null default 100,
 status text not null default 'PENDING', attempts int not null default 0, max_attempts int not null default 5,
 scheduled_at_utc timestamptz not null default now(), started_at_utc timestamptz null, finished_at_utc timestamptz null,
 last_error text null, correlation_id text not null, created_at_utc timestamptz not null default now(), updated_at_utc timestamptz not null default now()
);
create unique index if not exists ux_guardian_queue_pending_reason on ged.document_guardian_evaluation_queue(tenant_id, document_id, reason) where status in ('PENDING','PROCESSING');
create index if not exists ix_guardian_queue_acquire on ged.document_guardian_evaluation_queue(tenant_id, status, scheduled_at_utc, priority);

create table if not exists ged.dossier (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, dossier_type text not null, title text not null, status text not null default 'ACTIVE',
 metadata jsonb not null default '{}'::jsonb, owner_user_id uuid null, is_confidential boolean not null default false,
 completeness_score numeric(5,2) not null default 0, risk_score numeric(5,2) not null default 0, created_at_utc timestamptz not null default now(), updated_at_utc timestamptz not null default now(), reg_status char(1) not null default 'A'
);
create table if not exists ged.dossier_document (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, dossier_id uuid not null, document_id uuid not null,
 inclusion_mode text not null, rule_code text null, confidence numeric(5,2) not null default 100, status text not null default 'ACTIVE', created_by uuid null, created_at_utc timestamptz not null default now(), reg_status char(1) not null default 'A', unique(tenant_id,dossier_id,document_id)
);
create table if not exists ged.my_work_item (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, source text not null, source_id uuid null, item_type text not null, title text not null,
 priority text not null default 'NORMAL', due_at_utc timestamptz null, responsible_user_id uuid null, status text not null default 'PENDING', main_action text not null, link text not null, created_at_utc timestamptz not null default now(), updated_at_utc timestamptz not null default now(), reg_status char(1) not null default 'A'
);
create table if not exists ged.document_sla_instance (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, task_type text not null, source_id uuid not null, priority text not null default 'NORMAL', state text not null default 'ON_TIME', due_at_utc timestamptz not null, suspended_at_utc timestamptz null, completed_at_utc timestamptz null, escalation_level int not null default 0, history jsonb not null default '[]'::jsonb, created_at_utc timestamptz not null default now(), updated_at_utc timestamptz not null default now()
);

insert into ged.schema_migration_history(script_name, notes) values ('2026_07_guardian_consolidation_queue_events_dossiers.sql','Outbox, fila persistente do Guardião, dossiês, Meu Trabalho e SLA documental.') on conflict (script_name) do nothing;
