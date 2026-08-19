-- Central de Etiquetas: migração aditiva e idempotente.
create extension if not exists pgcrypto;
alter table ged.label_print_history add column if not exists print_mode varchar(30);
alter table ged.label_print_history add column if not exists template_name text;
alter table ged.label_print_history add column if not exists subject_type varchar(40);
alter table ged.label_print_history add column if not exists control_number text;
alter table ged.label_print_history add column if not exists location text;

create table if not exists ged.label_template_catalog (
 id uuid primary key default gen_random_uuid(), code varchar(80) not null unique, name text not null,
 print_mode varchar(30) not null, subject_type varchar(40) not null, view_name text not null,
 version varchar(20) not null default '1', description text null, supports_batch boolean not null default true,
 allows_manual_fields boolean not null default false, is_system_template boolean not null default false,
 is_active boolean not null default true, display_order integer not null default 0, created_at timestamptz not null default now()
);
insert into ged.label_template_catalog(code,name,print_mode,subject_type,view_name,description,allows_manual_fields,is_system_template,display_order) values
('FACTORY_BOX_V1','Padrão do Sistema - Caixa','FACTORY','BOX','BoxLabel','Etiqueta padrão do InovaGED para caixas físicas.',false,true,10),
('FACTORY_DOCUMENT_V1','Padrão do Sistema - Documento/Pasta','FACTORY','DOCUMENT','DocumentLabel','Etiqueta padrão do InovaGED para documentos e pastas.',false,true,20),
('LOCDESK_CAIXA_V1','LocDesk - Caixa','CUSTOM','BOX','LocDeskBoxLabel','Modelo personalizado LocDesk para identificação de caixas físicas.',true,false,30),
('LOCDESK_PASTA_V1','LocDesk - Pasta','CUSTOM','DOCUMENT','LocDeskFolderLabel','Modelo personalizado LocDesk para identificação de pastas/documentos.',true,false,40)
on conflict(code) do update set name=excluded.name,print_mode=excluded.print_mode,subject_type=excluded.subject_type,
 view_name=excluded.view_name,description=excluded.description,allows_manual_fields=excluded.allows_manual_fields,
 is_system_template=excluded.is_system_template,display_order=excluded.display_order,is_active=true;
create index if not exists ix_label_template_catalog_mode_subject on ged.label_template_catalog(print_mode,subject_type,is_active);
create index if not exists ix_label_print_history_mode_template on ged.label_print_history(tenant_id,print_mode,template_code);
create index if not exists ix_label_print_history_control_location on ged.label_print_history(tenant_id,control_number,location);
