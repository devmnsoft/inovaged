begin;
create table if not exists ged.label_manual_instance(
 id uuid primary key, tenant_id uuid not null, template_key varchar(120) not null, template_version integer not null,
 values_json jsonb not null default '{}'::jsonb, branding_profile_id uuid null, status varchar(20) not null default 'DRAFT',
 created_by uuid not null, created_at timestamptz not null default now(), updated_by uuid null, updated_at timestamptz null,
 reg_status varchar(16) not null default 'ACTIVE',
 constraint ck_label_manual_instance_status check(status in('DRAFT','PRINTED','ARCHIVED')),
 constraint ck_label_manual_instance_reg_status check(reg_status in('ACTIVE','INACTIVE'))
);
create index if not exists ix_label_manual_instance_tenant_updated on ged.label_manual_instance(tenant_id,updated_at desc) where reg_status='ACTIVE';
create index if not exists ix_label_manual_instance_template on ged.label_manual_instance(tenant_id,template_key,template_version) where reg_status='ACTIVE';
commit;
