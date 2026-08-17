-- InovaGED: trilha operacional complementar para despachos e decisões de inventário.
-- Totalmente aditiva e idempotente; tenant_id nunca é informado pela interface web.
create schema if not exists ged;

create table if not exists ged.protocol_dispatch (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    protocol_request_id uuid not null,
    movement_id uuid null,
    dispatch_text text not null,
    created_by uuid not null,
    created_at timestamptz not null default now(),
    reg_status char(1) not null default 'A',
    constraint ck_protocol_dispatch_text check (length(trim(dispatch_text)) > 0),
    constraint ck_protocol_dispatch_reg_status check (reg_status in ('A','I'))
);

create index if not exists ix_protocol_dispatch_tenant_protocol_created
    on ged.protocol_dispatch (tenant_id, protocol_request_id, created_at desc)
    where reg_status = 'A';

create table if not exists ged.physical_inventory_decision (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    box_id uuid not null,
    decision text not null,
    justification text not null,
    decided_by uuid not null,
    decided_at timestamptz not null default now(),
    details_json jsonb null,
    reg_status char(1) not null default 'A',
    constraint ck_physical_inventory_decision_justification check (length(trim(justification)) > 0),
    constraint ck_physical_inventory_decision_reg_status check (reg_status in ('A','I'))
);

create index if not exists ix_physical_inventory_decision_tenant_box_decided
    on ged.physical_inventory_decision (tenant_id, box_id, decided_at desc)
    where reg_status = 'A';
