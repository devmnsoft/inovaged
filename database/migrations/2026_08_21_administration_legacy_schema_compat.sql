-- Compatibilidade idempotente das telas e métricas administrativas com schemas legados.
create schema if not exists ged;

alter table if exists ged.app_user add column if not exists reg_status char(1) not null default 'A';
alter table if exists ged.app_user add column if not exists is_active boolean not null default true;
alter table if exists ged.app_user add column if not exists deleted_at_utc timestamptz null;
alter table if exists ged.app_user add column if not exists is_locked boolean not null default false;
alter table if exists ged.tenant add column if not exists reg_status char(1) not null default 'A';
alter table if exists ged.tenant add column if not exists is_active boolean not null default true;
alter table if exists ged.app_role add column if not exists reg_status char(1) not null default 'A';
alter table if exists ged.permission add column if not exists reg_status char(1) not null default 'A';

do $$ begin
  if to_regclass('ged.app_user') is not null
     and exists(select 1 from information_schema.columns where table_schema='ged' and table_name='app_user' and column_name='tenant_id') then
    create index if not exists ix_app_user_tenant_reg_status on ged.app_user(tenant_id,reg_status) where reg_status='A';
  end if;
  if to_regclass('ged.tenant') is not null then
    create index if not exists ix_tenant_reg_status on ged.tenant(reg_status) where reg_status='A';
  end if;
end $$;
