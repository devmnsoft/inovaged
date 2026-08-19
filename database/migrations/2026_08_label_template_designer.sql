-- Catálogo versionado de modelos de etiqueta. Seguro para reaplicação.
create extension if not exists pgcrypto;
create schema if not exists ged;

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
create table if not exists ged.label_template_config (
 id uuid primary key default gen_random_uuid(), template_id uuid not null references ged.label_template(id),
 tenant_id uuid null, header_text text null, logo_svg text null, primary_color varchar(20) null,
 secondary_color varchar(20) null, border_color varchar(20) null, text_color varchar(20) null,
 accent_color varchar(20) null, page_size varchar(20) not null default 'A4',
 label_width_mm numeric(10,2) null, label_height_mm numeric(10,2) null,
 labels_per_page integer not null default 1, orientation varchar(20) not null default 'PORTRAIT',
 custom_css text null, margin_top_mm numeric(10,2) not null default 0, margin_right_mm numeric(10,2) not null default 0,
 margin_bottom_mm numeric(10,2) not null default 0, margin_left_mm numeric(10,2) not null default 0,
 created_at timestamptz not null default now(), updated_at timestamptz null, reg_status char(1) not null default 'A'
);
create table if not exists ged.label_template_field (
 id uuid primary key default gen_random_uuid(), template_id uuid not null references ged.label_template(id),
 field_key varchar(100) not null, field_label text not null, field_type varchar(40) not null default 'TEXT',
 is_visible boolean not null default true, is_required boolean not null default false,
 is_editable boolean not null default true, default_value text null, display_order integer not null default 0,
 css_class text null, created_at timestamptz not null default now(), updated_at timestamptz null,
 reg_status char(1) not null default 'A'
);
create table if not exists ged.label_template_version (
 id uuid primary key default gen_random_uuid(), template_id uuid not null references ged.label_template(id),
 version_number integer not null, snapshot_json jsonb not null, published_by uuid null,
 published_at timestamptz not null default now(), change_notes text null, reg_status char(1) not null default 'A',
 unique(template_id, version_number)
);
create index if not exists ix_label_template_tenant_mode_subject on ged.label_template(tenant_id,print_mode,subject_type,is_active,reg_status);
create unique index if not exists ux_label_template_code on ged.label_template(code);
create index if not exists ix_label_template_code on ged.label_template(code);
create unique index if not exists ux_label_template_config_active on ged.label_template_config(template_id) where reg_status='A';
create unique index if not exists ux_label_template_field_active on ged.label_template_field(template_id,field_key) where reg_status='A';
create index if not exists ix_label_template_field_template on ged.label_template_field(template_id,display_order);
create index if not exists ix_label_template_version_template on ged.label_template_version(template_id,version_number desc);
create unique index if not exists ux_label_template_active_default on ged.label_template(coalesce(tenant_id,'00000000-0000-0000-0000-000000000000'::uuid),subject_type,print_mode) where is_active and is_default and reg_status='A';

insert into ged.label_template(code,name,description,print_mode,subject_type,view_name,is_system_template,is_custom_template,is_active,is_default,supports_batch,allows_manual_fields,display_order)
values
 ('FACTORY_BOX_V1','Padrão do Sistema - Caixa','Etiqueta interna e estruturalmente protegida do InovaGED.','FACTORY','BOX','BoxLabel',true,false,true,true,true,false,10),
 ('FACTORY_DOCUMENT_V1','Padrão do Sistema - Documento/Pasta','Etiqueta interna e estruturalmente protegida do InovaGED.','FACTORY','DOCUMENT','DocumentLabel',true,false,true,true,true,false,20),
 ('LOCDESK_CAIXA_V1','LocDesk - Caixa','Modelo personalizado LocDesk para caixas físicas.','CUSTOM','BOX','LocDeskBoxLabel',false,true,true,true,true,true,30),
 ('LOCDESK_PASTA_V1','LocDesk - Pasta','Modelo personalizado LocDesk para pastas e documentos.','CUSTOM','DOCUMENT','LocDeskFolderLabel',false,true,true,true,true,true,40)
on conflict(code) do nothing;

insert into ged.label_template_config(template_id,header_text,primary_color,secondary_color,border_color,text_color,accent_color,page_size,label_width_mm,label_height_mm,labels_per_page,orientation)
select id,'ARQUIVO LOCDESCK ANANINDEUA','#008a9a','#6fc7c8','#111111','#111111','#d60000','A4',190,130,case when subject_type='BOX' then 2 else 1 end,'PORTRAIT'
from ged.label_template where code in ('LOCDESK_CAIXA_V1','LOCDESK_PASTA_V1')
on conflict do nothing;

with fields(field_key,field_label,display_order) as (values
 ('control_number','N° de Controle',10),('volume','Volume',20),('subject','Assunto',30),('details','Detalhamento',40),
 ('activity','Atividade',50),('classification','Classificação',60),('support','Suporte',70),('document_period','Período do Documento',80),
 ('current_phase','Fase Atual',90),('disposal_forecast','Previsão Eliminação',100),('disposal_status','Situação Eliminação',110),
 ('led_number','Nº LED',120),('location','LOCALIZAÇÃO',130))
insert into ged.label_template_field(template_id,field_key,field_label,display_order)
select t.id,f.field_key,f.field_label,f.display_order from ged.label_template t cross join fields f
where t.code in ('LOCDESK_CAIXA_V1','LOCDESK_PASTA_V1') on conflict do nothing;

create table if not exists ged.permission (
 code text primary key, name text null, description text null,
 created_at timestamptz not null default now(), reg_status char(1) not null default 'A'
);
insert into ged.permission(code,name,description) values
 ('labels.view','Visualizar Central de Etiquetas','Permite consultar a central e o histórico de etiquetas.'),
 ('labels.print','Imprimir etiquetas','Permite gerar e imprimir etiquetas.'),
 ('labels.reprint','Reimprimir etiquetas','Permite reimprimir mediante justificativa.'),
 ('labels.templates.view','Visualizar modelos de etiqueta','Permite consultar catálogo, prévias e versões.'),
 ('labels.templates.manage','Gerenciar modelos de etiqueta','Permite criar, editar, ativar e definir modelos padrão.'),
 ('labels.templates.publish','Publicar modelos de etiqueta','Permite publicar snapshots versionados de modelos.')
on conflict(code) do update set name=excluded.name,description=excluded.description,reg_status='A';
