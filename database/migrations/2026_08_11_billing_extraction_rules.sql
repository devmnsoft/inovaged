-- Regras configuráveis por tenant aplicadas antes das heurísticas de faturamento.
create schema if not exists ged;
create table if not exists ged.billing_extraction_rule (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    name text not null,
    document_kind text not null,
    target_field text not null,
    keyword text null,
    regex_pattern text null,
    priority int not null default 100,
    is_required boolean not null default false,
    is_active boolean not null default true,
    created_by uuid null,
    created_at timestamptz not null default now(),
    updated_by uuid null,
    updated_at timestamptz null,
    reg_status char(1) not null default 'A'
);
create index if not exists ix_billing_rule_tenant_priority
 on ged.billing_extraction_rule(tenant_id, priority, name) where reg_status='A';
do $$ begin
 alter table ged.billing_extraction_rule add constraint ck_billing_rule_priority check(priority between 1 and 10000) not valid;
exception when duplicate_object then null; end $$;
do $$ begin
 alter table ged.billing_extraction_rule add constraint ck_billing_rule_source check(nullif(btrim(keyword),'') is not null or nullif(btrim(regex_pattern),'') is not null) not valid;
exception when duplicate_object then null; end $$;
