-- InovaGED Guardião — migration idempotente e não destrutiva.
create schema if not exists ged;
create extension if not exists pgcrypto;

create table if not exists ged.schema_migration_history (
    id uuid primary key default gen_random_uuid(),
    script_name text not null unique,
    applied_at timestamptz not null default now(),
    checksum text null,
    notes text null
);

create table if not exists ged.document_twin (
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null, document_id uuid not null,
    completeness_score numeric(5,2) not null default 0, risk_score numeric(5,2) not null default 0,
    status text not null default 'ACTIVE', last_evaluated_at_utc timestamptz null, created_at_utc timestamptz not null default now(), updated_at_utc timestamptz not null default now(), reg_status char(1) not null default 'A',
    unique (tenant_id, document_id)
);

create table if not exists ged.document_relationship (
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null, document_id uuid not null, related_document_id uuid not null,
    relationship_type text not null, confidence numeric(5,2) not null default 100, evidence_summary text null,
    created_at_utc timestamptz not null default now(), created_by uuid null, reg_status char(1) not null default 'A'
);

create table if not exists ged.document_evidence (
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null, document_id uuid not null, source_type text not null,
    evidence_key text not null, evidence_value text null, excerpt text null, metadata jsonb null, confidence numeric(5,2) not null default 100,
    created_at_utc timestamptz not null default now(), reg_status char(1) not null default 'A'
);

create table if not exists ged.document_rule (
    id uuid primary key default gen_random_uuid(), tenant_id uuid null, code text not null, name text not null, category text not null,
    is_active boolean not null default true, created_at_utc timestamptz not null default now(), reg_status char(1) not null default 'A', unique (tenant_id, code)
);

create table if not exists ged.document_rule_version (
    id uuid primary key default gen_random_uuid(), tenant_id uuid null, rule_id uuid not null, version_no int not null,
    severity text not null, expression jsonb not null default '{}'::jsonb, recommendation text not null default '', published_at_utc timestamptz not null default now(), is_current boolean not null default true,
    reg_status char(1) not null default 'A', unique (rule_id, version_no)
);

create table if not exists ged.document_finding (
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null, document_id uuid not null, rule_id uuid null, rule_version_id uuid null,
    rule_code text not null, rule_version int not null default 1, severity text not null, category text not null, description text not null, recommendation text not null default '',
    confidence numeric(5,2) not null default 0, status text not null default 'OPEN', assigned_to uuid null, created_at_utc timestamptz not null default now(), resolved_at_utc timestamptz null, reg_status char(1) not null default 'A'
);

create table if not exists ged.document_finding_evidence (
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null, document_id uuid not null, finding_id uuid not null,
    evidence_id uuid null, source_type text not null, evidence_key text not null, evidence_value text null, excerpt text null, confidence numeric(5,2) not null default 0,
    created_at_utc timestamptz not null default now(), reg_status char(1) not null default 'A'
);

create table if not exists ged.document_finding_decision (
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null, document_id uuid not null, finding_id uuid not null,
    decision text not null, justification text not null, decided_by uuid not null, decided_by_name text null, decided_at_utc timestamptz not null default now()
);

create table if not exists ged.document_obligation (
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null, document_id uuid not null, obligation_type text not null,
    due_at_utc timestamptz null, status text not null default 'PENDING', description text null, source text null, created_at_utc timestamptz not null default now(), reg_status char(1) not null default 'A'
);

create table if not exists ged.document_completeness_profile (
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null, name text not null, scope_type text not null, scope_id uuid null,
    is_active boolean not null default true, created_at_utc timestamptz not null default now(), reg_status char(1) not null default 'A'
);

create table if not exists ged.document_completeness_requirement (
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null, profile_id uuid not null, requirement_code text not null, description text not null,
    required_document_type_id uuid null, required_metadata_key text null, sort_order int not null default 0, reg_status char(1) not null default 'A'
);

create table if not exists ged.emergency_access_request (
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null, document_id uuid not null, requested_by uuid not null,
    justification text not null, status text not null default 'ACTIVE', starts_at_utc timestamptz not null default now(), expires_at_utc timestamptz not null,
    reviewed_by uuid null, reviewed_at_utc timestamptz null, review_notes text null, legal_audit_id uuid null, created_at_utc timestamptz not null default now()
);

alter table ged.document_twin add column if not exists correlation_id text null;
alter table ged.document_finding add column if not exists human_decision_required boolean not null default false;

create index if not exists ix_document_twin_tenant_document on ged.document_twin(tenant_id, document_id) where reg_status='A';
create index if not exists ix_document_relationship_tenant_document on ged.document_relationship(tenant_id, document_id) where reg_status='A';
create index if not exists ix_document_finding_tenant_document_status on ged.document_finding(tenant_id, document_id, status) where reg_status='A';
create index if not exists ix_document_finding_evidence_finding on ged.document_finding_evidence(tenant_id, finding_id) where reg_status='A';
create index if not exists ix_document_obligation_due on ged.document_obligation(tenant_id, due_at_utc, status) where reg_status='A';
create index if not exists ix_emergency_access_active on ged.emergency_access_request(tenant_id, document_id, status, expires_at_utc);

insert into ged.schema_migration_history(script_name, notes)
values ('2026_07_document_guardian.sql', 'Cria estruturas idempotentes do InovaGED Guardião sem remover dados.')
on conflict (script_name) do nothing;

DO $$
BEGIN
    IF to_regclass('ged.document') IS NOT NULL THEN
        IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_document_twin_document') THEN
            ALTER TABLE ged.document_twin ADD CONSTRAINT fk_document_twin_document FOREIGN KEY (document_id) REFERENCES ged.document(id);
        END IF;
        IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_document_relationship_document') THEN
            ALTER TABLE ged.document_relationship ADD CONSTRAINT fk_document_relationship_document FOREIGN KEY (document_id) REFERENCES ged.document(id);
        END IF;
        IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_document_relationship_related_document') THEN
            ALTER TABLE ged.document_relationship ADD CONSTRAINT fk_document_relationship_related_document FOREIGN KEY (related_document_id) REFERENCES ged.document(id);
        END IF;
        IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_document_finding_document') THEN
            ALTER TABLE ged.document_finding ADD CONSTRAINT fk_document_finding_document FOREIGN KEY (document_id) REFERENCES ged.document(id);
        END IF;
        IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_document_obligation_document') THEN
            ALTER TABLE ged.document_obligation ADD CONSTRAINT fk_document_obligation_document FOREIGN KEY (document_id) REFERENCES ged.document(id);
        END IF;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_document_finding_evidence_finding') THEN
        ALTER TABLE ged.document_finding_evidence ADD CONSTRAINT fk_document_finding_evidence_finding FOREIGN KEY (finding_id) REFERENCES ged.document_finding(id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_document_finding_decision_finding') THEN
        ALTER TABLE ged.document_finding_decision ADD CONSTRAINT fk_document_finding_decision_finding FOREIGN KEY (finding_id) REFERENCES ged.document_finding(id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_document_rule_version_rule') THEN
        ALTER TABLE ged.document_rule_version ADD CONSTRAINT fk_document_rule_version_rule FOREIGN KEY (rule_id) REFERENCES ged.document_rule(id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_document_completeness_requirement_profile') THEN
        ALTER TABLE ged.document_completeness_requirement ADD CONSTRAINT fk_document_completeness_requirement_profile FOREIGN KEY (profile_id) REFERENCES ged.document_completeness_profile(id);
    END IF;
EXCEPTION WHEN duplicate_object THEN
    RAISE NOTICE 'Constraint concorrente já criada: %', SQLERRM;
END $$;
