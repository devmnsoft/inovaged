-- Label template designer schema. Safe to run repeatedly on new and legacy databases.
create schema if not exists ged;
create extension if not exists pgcrypto;

create table if not exists ged.label_template (
    id uuid primary key default gen_random_uuid(), tenant_id uuid null, code varchar(80) not null,
    name text not null, description text null, print_mode varchar(30) not null, subject_type varchar(40) not null,
    view_name text not null, version integer not null default 1, is_system_template boolean not null default false,
    is_custom_template boolean not null default false, is_active boolean not null default true,
    is_default boolean not null default false, supports_batch boolean not null default true,
    allows_manual_fields boolean not null default false, display_order integer not null default 0,
    created_at timestamptz not null default now(), created_by uuid null, updated_at timestamptz null,
    updated_by uuid null, reg_status char(1) not null default 'A'
);
alter table ged.label_template add column if not exists tenant_id uuid null;
alter table ged.label_template add column if not exists code varchar(80);
alter table ged.label_template add column if not exists name text;
alter table ged.label_template add column if not exists description text null;
alter table ged.label_template add column if not exists print_mode varchar(30);
alter table ged.label_template add column if not exists subject_type varchar(40);
alter table ged.label_template add column if not exists view_name text;
alter table ged.label_template add column if not exists version integer not null default 1;
alter table ged.label_template add column if not exists is_system_template boolean not null default false;
alter table ged.label_template add column if not exists is_custom_template boolean not null default false;
alter table ged.label_template add column if not exists is_active boolean not null default true;
alter table ged.label_template add column if not exists is_default boolean not null default false;
alter table ged.label_template add column if not exists supports_batch boolean not null default true;
alter table ged.label_template add column if not exists allows_manual_fields boolean not null default false;
alter table ged.label_template add column if not exists display_order integer not null default 0;
alter table ged.label_template add column if not exists created_at timestamptz not null default now();
alter table ged.label_template add column if not exists created_by uuid null;
alter table ged.label_template add column if not exists updated_at timestamptz null;
alter table ged.label_template add column if not exists updated_by uuid null;
alter table ged.label_template add column if not exists reg_status char(1) not null default 'A';

create unique index if not exists ux_label_template_tenant_code on ged.label_template
 (coalesce(tenant_id,'00000000-0000-0000-0000-000000000000'::uuid),code) where reg_status='A';
create index if not exists ix_label_template_tenant_mode_subject on ged.label_template(tenant_id,print_mode,subject_type,is_active,reg_status);
create index if not exists ix_label_template_code on ged.label_template(code);
create index if not exists ix_label_template_subject on ged.label_template(subject_type,print_mode,is_active);

create table if not exists ged.label_template_config (
 id uuid primary key default gen_random_uuid(), template_id uuid not null references ged.label_template(id), tenant_id uuid null,
 header_text text null, logo_svg text null, primary_color varchar(20) null, secondary_color varchar(20) null,
 border_color varchar(20) null, text_color varchar(20) null, accent_color varchar(20) null,
 page_size varchar(20) not null default 'A4', label_width_mm numeric(10,2) null, label_height_mm numeric(10,2) null,
 labels_per_page integer not null default 1, orientation varchar(20) not null default 'PORTRAIT', custom_css text null,
 margin_top_mm numeric(10,2) not null default 0, margin_right_mm numeric(10,2) not null default 0,
 margin_bottom_mm numeric(10,2) not null default 0, margin_left_mm numeric(10,2) not null default 0,
 created_at timestamptz not null default now(), updated_at timestamptz null, reg_status char(1) not null default 'A'
);
alter table ged.label_template_config add column if not exists margin_top_mm numeric(10,2) not null default 0;
alter table ged.label_template_config add column if not exists margin_right_mm numeric(10,2) not null default 0;
alter table ged.label_template_config add column if not exists margin_bottom_mm numeric(10,2) not null default 0;
alter table ged.label_template_config add column if not exists margin_left_mm numeric(10,2) not null default 0;
create table if not exists ged.label_template_field (
 id uuid primary key default gen_random_uuid(), template_id uuid not null references ged.label_template(id),
 field_key varchar(100) not null, field_label text not null, field_type varchar(40) not null default 'TEXT',
 is_visible boolean not null default true, is_required boolean not null default false, is_editable boolean not null default true,
 default_value text null, display_order integer not null default 0, css_class text null,
 created_at timestamptz not null default now(), updated_at timestamptz null, reg_status char(1) not null default 'A'
);
create table if not exists ged.label_template_version (
 id uuid primary key default gen_random_uuid(), template_id uuid not null references ged.label_template(id),
 version_number integer not null, snapshot_json jsonb not null, published_by uuid null,
 published_at timestamptz not null default now(), change_notes text null, reg_status char(1) not null default 'A',
 unique(template_id,version_number)
);
create index if not exists ix_label_template_config_template on ged.label_template_config(template_id);
create unique index if not exists ux_label_template_config_active on ged.label_template_config(template_id) where reg_status='A';
create unique index if not exists ux_label_template_field_active on ged.label_template_field(template_id,field_key) where reg_status='A';
create index if not exists ix_label_template_field_template on ged.label_template_field(template_id,display_order);
create index if not exists ix_label_template_version_template on ged.label_template_version(template_id,version_number desc);

-- Import the previous catalog first. A regular-expression conversion tolerates legacy version strings.
do $$
begin
 if to_regclass('ged.label_template_catalog') is not null then
  execute $copy$
   insert into ged.label_template(tenant_id,code,name,description,print_mode,subject_type,view_name,version,
    is_system_template,is_custom_template,is_active,supports_batch,allows_manual_fields,display_order,reg_status)
   select null,c.code,c.name,c.description,c.print_mode,c.subject_type,c.view_name,
    case when coalesce(c.version::text,'') ~ '^[0-9]+$' then c.version::text::integer else 1 end,
    c.is_system_template,not c.is_system_template,c.is_active,c.supports_batch,c.allows_manual_fields,c.display_order,'A'
   from ged.label_template_catalog c
   where not exists(select 1 from ged.label_template t where t.code=c.code and t.reg_status='A' and t.tenant_id is null)
  $copy$;
 end if;
end $$;

insert into ged.label_template(tenant_id,code,name,description,print_mode,subject_type,view_name,version,is_system_template,
 is_custom_template,is_active,is_default,supports_batch,allows_manual_fields,display_order,reg_status)
values
 (null,'FACTORY_BOX_V1','Padrão do Sistema - Caixa','Etiqueta padrão do InovaGED para caixas físicas.','FACTORY','BOX','BoxLabel',1,true,false,true,true,true,false,10,'A'),
 (null,'FACTORY_DOCUMENT_V1','Padrão do Sistema - Documento/Pasta','Etiqueta padrão do InovaGED para documentos e pastas.','FACTORY','DOCUMENT','DocumentLabel',1,true,false,true,true,true,false,20,'A'),
 (null,'LOCDESK_CAIXA_V1','LocDesk - Caixa','Modelo personalizado LocDesk para identificação de caixas físicas.','CUSTOM','BOX','LocDeskBoxLabel',1,false,true,true,true,true,true,30,'A'),
 (null,'LOCDESK_PASTA_V1','LocDesk - Pasta','Modelo personalizado LocDesk para identificação de pastas/documentos.','CUSTOM','DOCUMENT','LocDeskFolderLabel',1,false,true,true,true,true,true,40,'A')
on conflict (coalesce(tenant_id,'00000000-0000-0000-0000-000000000000'::uuid),code) where reg_status='A'
do update set name=excluded.name,description=excluded.description,print_mode=excluded.print_mode,
 subject_type=excluded.subject_type,view_name=excluded.view_name,version=excluded.version,
 is_system_template=excluded.is_system_template,is_custom_template=excluded.is_custom_template,is_active=true,
 supports_batch=excluded.supports_batch,allows_manual_fields=excluded.allows_manual_fields,
 display_order=excluded.display_order,updated_at=now();
