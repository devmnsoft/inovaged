-- Acervo fisico operacional. Evolui as tabelas canonicas; nao cria um modelo concorrente.
begin;

alter table ged.physical_location
  add column if not exists unit_name varchar(120),
  add column if not exists full_location_code varchar(500),
  add column if not exists updated_at timestamptz,
  add column if not exists updated_by uuid;

update ged.physical_location
set unit_name = coalesce(unit_name, property_name),
    full_location_code = upper(concat_ws('-', nullif(unit_name,''), nullif(building,''),
      nullif(room,''), nullif(aisle,''), nullif(rack,''), nullif(shelf,''), nullif(pallet,''),
      nullif(location_code,'')))
where full_location_code is null;

create unique index if not exists ux_physical_location_tenant_code
  on ged.physical_location (tenant_id, upper(full_location_code))
  where reg_status = 'A' and full_location_code is not null;

create table if not exists ged.physical_location_history (
  id uuid primary key default gen_random_uuid(), tenant_id uuid not null,
  location_id uuid not null, action varchar(30) not null,
  old_data jsonb, new_data jsonb, reason text,
  changed_at timestamptz not null default now(), changed_by uuid,
  reg_status char(1) not null default 'A'
);
create index if not exists ix_physical_location_history_lookup
  on ged.physical_location_history(tenant_id, location_id, changed_at desc);

alter table ged.box
  add column if not exists lifecycle_status varchar(20) not null default 'OPEN',
  add column if not exists is_full boolean not null default false,
  add column if not exists last_moved_at timestamptz,
  add column if not exists last_moved_by uuid;

do $$ begin
  alter table ged.box add constraint ck_box_lifecycle_status
    check (lifecycle_status in ('OPEN','CLOSED','ARCHIVED'));
exception when duplicate_object then null; end $$;

alter table ged.batch
  add column if not exists responsible_user_id uuid,
  add column if not exists progress smallint not null default 0,
  add column if not exists pending_notes text,
  add column if not exists updated_at timestamptz;

-- O status e o trigger de histórico compartilham o enum: evolua-o sem alterar colunas.
do $$
declare value text;
begin
  if to_regtype('ged.batch_status') is not null then
    foreach value in array array['RECEIVED','TRIAGE','PREPARATION','DIGITIZATION','INDEXING',
      'CONFERENCE','ARCHIVING','FINALIZED','CANCELLED'] loop
      execute format('alter type ged.batch_status add value if not exists %L', value);
    end loop;
  end if;
end $$;

commit;
