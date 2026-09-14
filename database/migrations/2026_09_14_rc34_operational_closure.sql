-- RC34: short-lived, user-scoped hand-off from intake to the existing BatchPrint flow.
create table if not exists ged.label_print_selection (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, user_id uuid not null,
 token_hash varchar(128) not null, subject_type varchar(32) not null, created_at timestamptz not null default now(),
 expires_at timestamptz not null, reg_status varchar(16) not null default 'A');
create unique index if not exists ux_label_print_selection_token on ged.label_print_selection(tenant_id,user_id,token_hash) where reg_status='A';
create index if not exists ix_label_print_selection_expiry on ged.label_print_selection(expires_at) where reg_status='A';
create table if not exists ged.label_print_selection_item (
 selection_id uuid not null references ged.label_print_selection(id) on delete cascade,
 subject_id uuid not null, primary key(selection_id,subject_id));

-- Reusable canvas blocks store normalized authoring data only, never rendered/runtime values.
create table if not exists ged.label_canvas_component_preset (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, name varchar(160) not null,
 category varchar(80) not null default 'Meus blocos', schema_version integer not null, elements_json jsonb not null,
 created_by uuid not null, created_at timestamptz not null default now(), updated_by uuid null, updated_at timestamptz null,
 archived_by uuid null, archived_at timestamptz null, reg_status varchar(16) not null default 'A');
create index if not exists ix_label_canvas_component_preset_tenant on ged.label_canvas_component_preset(tenant_id,lower(name)) where reg_status='A';

do $$ declare action_value text; begin
 foreach action_value in array array['INTAKE_REVIEWED','INTAKE_NEEDS_CORRECTION','INTAKE_RESET_PENDING','DOCUMENT_BULK_CLASSIFIED'] loop
  if exists(select 1 from pg_type t join pg_namespace n on n.oid=t.typnamespace where n.nspname='ged' and t.typname='audit_action_enum')
   and not exists(select 1 from pg_enum e join pg_type t on t.oid=e.enumtypid join pg_namespace n on n.oid=t.typnamespace where n.nspname='ged' and t.typname='audit_action_enum' and e.enumlabel=action_value)
  then execute format('alter type ged.audit_action_enum add value %L',action_value); end if;
 end loop;
end $$;
