-- Classification & Retention Plan 2.0. Idempotent, additive and tenant scoped.
create schema if not exists ged;
create extension if not exists pgcrypto;
create table if not exists ged.classification_node (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, parent_id uuid null,
 code text not null, title text not null, description text null, activity_type varchar(40) null,
 document_function text null, normative_source text null, keywords text null, display_order integer not null default 0,
 review_status varchar(40) not null default 'DRAFT', is_active boolean not null default true,
 created_at timestamptz not null default now(), updated_at timestamptz null, reg_status char(1) not null default 'A',
 constraint fk_classification_node_parent foreign key (parent_id) references ged.classification_node(id)
);
create table if not exists ged.retention_rule_v2 (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null,
 classification_node_id uuid not null references ged.classification_node(id), current_phase_years integer null,
 intermediate_phase_years integer null, final_destination varchar(80) not null default 'REVIEW', trigger_event text null,
 trigger_description text null, legal_basis text null, observation text null, review_status varchar(40) not null default 'DRAFT',
 effective_from date null, effective_to date null, created_at timestamptz not null default now(), updated_at timestamptz null,
 reg_status char(1) not null default 'A', check (current_phase_years is null or current_phase_years >= 0),
 check (intermediate_phase_years is null or intermediate_phase_years >= 0), check (effective_to is null or effective_from is null or effective_to >= effective_from)
);
create table if not exists ged.classification_plan_version_v2 (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, version_number integer not null, title text not null,
 description text null, status varchar(40) not null default 'DRAFT', published_at timestamptz null, published_by uuid null,
 notes text null, snapshot_json jsonb null, created_at timestamptz not null default now(), reg_status char(1) not null default 'A'
);
create table if not exists ged.classification_change_request (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, classification_node_id uuid null,
 request_type varchar(60) not null, title text not null, description text null, reason text null, status varchar(40) not null default 'OPEN',
 requested_by uuid null, requested_at timestamptz not null default now(), reviewed_by uuid null, reviewed_at timestamptz null,
 review_notes text null, reg_status char(1) not null default 'A'
);
create unique index if not exists ux_classification_node_tenant_code on ged.classification_node(tenant_id, code) where reg_status='A';
create index if not exists ix_classification_node_parent on ged.classification_node(tenant_id,parent_id,display_order) where reg_status='A';
create unique index if not exists ux_retention_rule_v2_active_classification on ged.retention_rule_v2(tenant_id,classification_node_id) where reg_status='A';
create index if not exists ix_retention_rule_v2_classification on ged.retention_rule_v2(tenant_id,classification_node_id) where reg_status='A';
create unique index if not exists ux_classification_plan_version_v2 on ged.classification_plan_version_v2(tenant_id,version_number) where reg_status='A';
create index if not exists ix_classification_change_request_status on ged.classification_change_request(tenant_id,status,requested_at desc) where reg_status='A';
