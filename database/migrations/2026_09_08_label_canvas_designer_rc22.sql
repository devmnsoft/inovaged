-- RC22 - Designer visual de etiquetas em canvas. Idempotente e compatível com o Designer 2.0.
create schema if not exists ged;
create extension if not exists pgcrypto;

create table if not exists ged.label_template_design (
 id uuid primary key default gen_random_uuid(), tenant_id uuid null, template_code text not null,
 template_key varchar(120) null, template_name varchar(200) not null, description text null,
 template_kind varchar(60) null, print_mode varchar(40) not null default 'CUSTOM', subject_type varchar(60) not null,
 view_name text null, paper_kind varchar(40) null, paper_size varchar(40) not null default 'A4',
 width_mm numeric(10,2) not null, height_mm numeric(10,2) not null, orientation varchar(20) not null default 'portrait',
 status varchar(30) not null default 'DRAFT', design_json jsonb null, current_version integer not null default 1,
 is_system_template boolean not null default false, base_template_code text null, created_by uuid null,
 created_at timestamptz not null default now(), updated_by uuid null, updated_at timestamptz null,
 published_by uuid null, published_at timestamptz null, archived_at timestamptz null,
 reg_status varchar(20) not null default 'ACTIVE'
);
alter table ged.label_template_design add column if not exists template_key varchar(120);
alter table ged.label_template_design add column if not exists template_kind varchar(60);
alter table ged.label_template_design add column if not exists paper_kind varchar(40);
alter table ged.label_template_design add column if not exists updated_by uuid null;
alter table ged.label_template_design add column if not exists published_by uuid null;
alter table ged.label_template_design add column if not exists published_at timestamptz null;
alter table ged.label_template_design add column if not exists archived_at timestamptz null;
alter table ged.label_template_design alter column reg_status type varchar(20) using case when reg_status::text='A' then 'ACTIVE' when reg_status::text='I' then 'INACTIVE' else reg_status::text end;
alter table ged.label_template_design alter column reg_status set default 'ACTIVE';
update ged.label_template_design set template_key=coalesce(template_key,template_code),
 template_kind=coalesce(template_kind,case when template_code like '%_CANVAS_%' then 'CANVAS' else 'CLASSIC' end),
 paper_kind=coalesce(paper_kind,paper_size,'A4'),
 reg_status=case when reg_status='A' then 'ACTIVE' when reg_status='I' then 'INACTIVE' else reg_status end,
 design_json=coalesce(design_json,jsonb_build_object('schemaVersion',1,'canvas',jsonb_build_object('widthMm',width_mm,'heightMm',height_mm,'paper',coalesce(paper_size,'A4'),'orientation',lower(orientation),'gridMm',2,'safeMarginMm',3),'elements','[]'::jsonb,'bindings',jsonb_build_object('subjectType',subject_type,'sampleDataProfile','Documento GED')));
alter table ged.label_template_design alter column template_key set not null;
alter table ged.label_template_design alter column template_kind set not null;
alter table ged.label_template_design alter column paper_kind set not null;
alter table ged.label_template_design alter column design_json set not null;

create table if not exists ged.label_template_design_version (
 id uuid primary key default gen_random_uuid(), tenant_id uuid null,
 template_design_id uuid not null references ged.label_template_design(id), version_number integer not null default 1,
 version_no integer null, status varchar(30) not null default 'PUBLISHED', snapshot_json jsonb not null default '{}'::jsonb,
 design_json jsonb null, notes text null, change_summary text null, created_by uuid null,
 created_at timestamptz not null default now(), published_by uuid null, published_at timestamptz null,
 snapshot_hash varchar(128) null, reg_status varchar(20) not null default 'ACTIVE'
);
alter table ged.label_template_design_version add column if not exists version_no integer;
alter table ged.label_template_design_version add column if not exists design_json jsonb;
alter table ged.label_template_design_version add column if not exists change_summary text null;
alter table ged.label_template_design_version add column if not exists created_by uuid null;
alter table ged.label_template_design_version add column if not exists created_at timestamptz not null default now();
alter table ged.label_template_design_version add column if not exists snapshot_hash varchar(128) null;
alter table ged.label_template_design_version alter column reg_status type varchar(20) using case when reg_status::text='A' then 'ACTIVE' when reg_status::text='I' then 'INACTIVE' else reg_status::text end;
alter table ged.label_template_design_version alter column reg_status set default 'ACTIVE';
update ged.label_template_design_version set version_no=coalesce(version_no,version_number),
 design_json=coalesce(design_json,snapshot_json),change_summary=coalesce(change_summary,notes),
 created_at=coalesce(created_at,published_at,now()),reg_status=case when reg_status='A' then 'ACTIVE' when reg_status='I' then 'INACTIVE' else reg_status end;
alter table ged.label_template_design_version alter column version_no set not null;
alter table ged.label_template_design_version alter column design_json set not null;

create table if not exists ged.label_template_design_event (
 id uuid primary key default gen_random_uuid(), tenant_id uuid null,
 template_design_id uuid not null references ged.label_template_design(id), event_type varchar(80) not null,
 event_message text null, payload_json jsonb null, created_by uuid null, created_at timestamptz not null default now(),
 ip_address varchar(80) null, user_agent text null
);

drop index if exists ged.ux_label_template_design_code;
create unique index if not exists ux_label_template_design_tenant_key on ged.label_template_design
 (coalesce(tenant_id,'00000000-0000-0000-0000-000000000000'::uuid),template_key) where reg_status in ('A','ACTIVE');
create index if not exists ix_label_template_design_key on ged.label_template_design(template_key);
create index if not exists ix_label_template_design_tenant on ged.label_template_design(tenant_id);
create index if not exists ix_label_template_design_status on ged.label_template_design(status);
create index if not exists ix_label_template_design_subject on ged.label_template_design(subject_type);
create index if not exists ix_label_template_design_kind on ged.label_template_design(template_kind);
create index if not exists ix_label_template_design_created on ged.label_template_design(created_at desc);
create index if not exists ix_label_template_design_json_gin on ged.label_template_design using gin(design_json);
drop index if exists ged.ux_label_template_design_version;
create unique index if not exists ux_label_template_design_version_no on ged.label_template_design_version(template_design_id,version_no) where reg_status in ('A','ACTIVE');
create index if not exists ix_label_template_design_version_created on ged.label_template_design_version(template_design_id,created_at desc);
create index if not exists ix_label_template_design_event_created on ged.label_template_design_event(template_design_id,created_at desc);
create index if not exists ix_label_template_design_event_tenant on ged.label_template_design_event(tenant_id,event_type,created_at desc);

-- Os cinco modelos canvas começam como rascunhos editáveis. Os modelos clássicos permanecem intactos.
with seed(template_key,template_name,template_kind,subject_type,width_mm,height_mm,profile,primary_field,secondary_field) as (values
 ('LOCDESK_PASTA_CANVAS_V1','LocDesk - Pasta (Canvas)','LOCDESK','LocDeskFolder',174::numeric,110::numeric,'LocDesk Pasta','controlNumber','location'),
 ('LOCDESK_CAIXA_CANVAS_V1','LocDesk - Caixa (Canvas)','LOCDESK','LocDeskBox',174::numeric,110::numeric,'LocDesk Caixa','controlNumber','location'),
 ('HOL_PRONTUARIO_CANVAS_V1','HOL - Prontuário (Canvas)','HOL','LocDeskFolder',174::numeric,110::numeric,'HOL','controlNumber','location'),
 ('GED_DOCUMENTO_CANVAS_V1','GED - Documento (Canvas)','GED','Document',100::numeric,70::numeric,'Documento GED','documentCode','documentTitle'),
 ('GED_CAIXA_CANVAS_V1','GED - Caixa (Canvas)','GED','Box',100::numeric,70::numeric,'Caixa GED','boxCode','location')
), hol_field(id,name,field_key,x,y,w,h,font_size,color,weight) as (values
 ('contract','Contrato','contractName',42,14,96,8,8,'#111111','400'),('control','Nº Controle','controlNumber',42,22,52,8,10,'#b42318','700'),
 ('volume','Volume','volumeNumber',94,22,44,8,10,'#b42318','700'),('subject','Assunto','subject',5,32,133,10,8,'#111111','700'),
 ('details','Detalhamento','details',5,42,85,10,7,'#111111','400'),('activity','Atividade','activity',90,42,48,10,7,'#111111','400'),
 ('classification','Classificação','classification',5,52,133,12,7,'#111111','400'),('support','Suporte','support',5,64,42,9,7,'#111111','400'),
 ('period','Período do Documento','documentPeriod',47,64,91,9,7,'#111111','400'),('phase','Fase Atual','currentPhase',5,73,63,9,6,'#111111','400'),
 ('forecast','Previsão Eliminação','eliminationForecast',68,73,70,9,6,'#111111','400'),('elimination','Situação Eliminação','eliminationStatus',5,82,76,9,6,'#111111','400'),
 ('led','Nº LED','ledNumber',81,82,57,9,7,'#111111','400'),('location','Localização','location',5,91,133,12,9,'#111111','700'),
 ('trace','Rastreio','traceCode',142,34,26,8,6,'#111111','400')
), hol_elements as (
 select jsonb_agg(jsonb_build_object('id',id,'type','field','name',name,'xMm',x,'yMm',y,'widthMm',w,'heightMm',h,
  'rotationDeg',0,'zIndex',12,'locked',false,'visible',true,
  'style',jsonb_build_object('fontFamily','Arial','fontSizePt',font_size,'fontWeight',weight,'align',case when id in ('control','volume','location','trace') then 'center' else 'left' end,'color',color,'backgroundColor','transparent','border',case when id='location' then '2px solid #111111' when id='trace' then 'none' else '1px solid #111111' end,'borderRadiusMm',0,'paddingMm',1,'wrap',true),
  'binding',jsonb_build_object('field',field_key),'validation',jsonb_build_object('required',true,'showOnlyWhenValue',false)) order by y,x) elements from hol_field
), designs as (
 select s.*,jsonb_build_object('schemaVersion',1,
  'canvas',jsonb_build_object('widthMm',s.width_mm,'heightMm',s.height_mm,'paper','A4','orientation','portrait','gridMm',2,'safeMarginMm',3),
  'elements',case when s.profile='HOL' then
   jsonb_build_array(
    jsonb_build_object('id','title','type','text','name','Cabeçalho oficial','xMm',5,'yMm',4,'widthMm',133,'heightMm',9,'rotationDeg',0,'zIndex',20,'locked',false,'visible',true,'text','ARQUIVO LOCDESCK ANANINDEUA','style',jsonb_build_object('fontFamily','Arial','fontSizePt',11,'fontWeight','700','align','center','color','#111111','backgroundColor','transparent','border','none','borderRadiusMm',0,'paddingMm',1,'wrap',true),'validation',jsonb_build_object('required',true,'showOnlyWhenValue',false)),
    jsonb_build_object('id','logo','type','logo','name','Logo LocDesk','xMm',5,'yMm',14,'widthMm',35,'heightMm',16,'rotationDeg',0,'zIndex',20,'locked',false,'visible',true,'style',jsonb_build_object('fontFamily','Arial','fontSizePt',9,'fontWeight','700','align','center','color','#111111','backgroundColor','transparent','border','1px solid #111111','borderRadiusMm',0,'paddingMm',1,'wrap',true),'binding',jsonb_build_object('fallback','LocDesk'),'validation',jsonb_build_object('required',false,'showOnlyWhenValue',false))
   ) || (select elements from hol_elements) || jsonb_build_array(
    jsonb_build_object('id','qr','type','qr','name','QR Code','xMm',142,'yMm',6,'widthMm',26,'heightMm',26,'rotationDeg',0,'zIndex',30,'locked',false,'visible',true,'style',jsonb_build_object('fontFamily','Arial','fontSizePt',7,'fontWeight','400','align','center','color','#111111','backgroundColor','#ffffff','border','none','borderRadiusMm',0,'paddingMm',1,'wrap',false),'binding',jsonb_build_object('field','qrPayload'),'validation',jsonb_build_object('required',true,'showOnlyWhenValue',false))
   )
  else jsonb_build_array(
   jsonb_build_object('id','title','type','text','name','Título da etiqueta','xMm',5,'yMm',5,'widthMm',70,'heightMm',9,'rotationDeg',0,'zIndex',10,'locked',false,'visible',true,'text',case when s.template_kind='LOCDESK' then 'ARQUIVO LOCDESCK ANANINDEUA' else s.template_name end,'style',jsonb_build_object('fontFamily','Arial','fontSizePt',10,'fontWeight','700','align','center','color','#111111','backgroundColor','transparent','border','none','borderRadiusMm',0,'paddingMm',1,'wrap',true),'validation',jsonb_build_object('required',true,'showOnlyWhenValue',false)),
   jsonb_build_object('id','code','type','field','name','Código de identificação','xMm',5,'yMm',18,'widthMm',60,'heightMm',12,'rotationDeg',0,'zIndex',11,'locked',false,'visible',true,'style',jsonb_build_object('fontFamily','Arial','fontSizePt',10,'fontWeight','700','align','left','color','#111111','backgroundColor','transparent','border','1px solid #111111','borderRadiusMm',0,'paddingMm',1,'wrap',true),'binding',jsonb_build_object('field',s.primary_field),'validation',jsonb_build_object('required',true,'showOnlyWhenValue',false)),
   jsonb_build_object('id','secondary','type','field','name','Informação complementar','xMm',5,'yMm',34,'widthMm',60,'heightMm',12,'rotationDeg',0,'zIndex',11,'locked',false,'visible',true,'style',jsonb_build_object('fontFamily','Arial','fontSizePt',8,'fontWeight','400','align','left','color','#111111','backgroundColor','transparent','border','1px solid #111111','borderRadiusMm',0,'paddingMm',1,'wrap',true),'binding',jsonb_build_object('field',s.secondary_field),'validation',jsonb_build_object('required',false,'showOnlyWhenValue',false)),
   jsonb_build_object('id','qr','type','qr','name','QR Code','xMm',s.width_mm-28,'yMm',5,'widthMm',23,'heightMm',23,'rotationDeg',0,'zIndex',20,'locked',false,'visible',true,'style',jsonb_build_object('fontFamily','Arial','fontSizePt',7,'fontWeight','400','align','center','color','#111111','backgroundColor','#ffffff','border','none','borderRadiusMm',0,'paddingMm',1,'wrap',false),'binding',jsonb_build_object('field','qrPayload'),'validation',jsonb_build_object('required',true,'showOnlyWhenValue',false))
  ) end,
  'bindings',jsonb_build_object('subjectType',s.subject_type,'sampleDataProfile',s.profile)) design_json from seed s
)
insert into ged.label_template_design(id,tenant_id,template_code,template_key,template_name,description,template_kind,print_mode,subject_type,view_name,paper_kind,paper_size,width_mm,height_mm,orientation,status,design_json,current_version,is_system_template,created_at,reg_status)
select gen_random_uuid(),null,d.template_key,d.template_key,d.template_name,'Template visual RC22 editável e versionado.',d.template_kind,'CUSTOM',d.subject_type,'CanvasLabel','A4','A4',d.width_mm,d.height_mm,'portrait','DRAFT',d.design_json,1,false,now(),'ACTIVE'
from designs d where not exists(select 1 from ged.label_template_design x where x.template_key=d.template_key and x.reg_status in ('A','ACTIVE'));
