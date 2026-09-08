-- RC23 - Canvas multi-cliente sobre a identidade visual oficial. Idempotente e aditivo.
create schema if not exists ged;

alter table ged.label_template_design add column if not exists default_branding_profile_id uuid null references ged.print_branding_profile(id);
alter table ged.label_template_design add column if not exists branding_binding_key varchar(160) null;
alter table ged.label_template_design add column if not exists client_name_fallback text null;
alter table ged.label_template_design add column if not exists contract_name_fallback text null;
alter table ged.label_template_design add column if not exists organization_name_fallback text null;
alter table ged.label_template_design add column if not exists header_title_fallback text null;
alter table ged.label_template_design add column if not exists header_subtitle_fallback text null;
alter table ged.label_template_design add column if not exists label_context varchar(40) not null default 'GENERIC';

create index if not exists ix_label_template_design_branding_profile on ged.label_template_design(default_branding_profile_id) where default_branding_profile_id is not null;
create index if not exists ix_label_template_design_context on ged.label_template_design(label_context);

update ged.label_template_design
   set label_context='LEGACY', branding_binding_key=coalesce(branding_binding_key,template_key)
 where (template_key like 'LOCDESK_%' or template_key like 'HOL_%') and label_context='GENERIC';
update ged.label_template_design
   set branding_binding_key=coalesce(branding_binding_key,template_key)
 where branding_binding_key is null;

with seed(template_key,template_name,subject_type,width_mm,height_mm,primary_field,secondary_field) as (values
 ('CLIENTE_CAIXA_CANVAS_V1','Cliente - Caixa (Canvas)','Box',174::numeric,110::numeric,'boxCode','location'),
 ('CLIENTE_DOCUMENTO_CANVAS_V1','Cliente - Documento (Canvas)','Document',100::numeric,70::numeric,'documentCode','documentTitle'),
 ('CLIENTE_PASTA_CANVAS_V1','Cliente - Pasta (Canvas)','Folder',174::numeric,110::numeric,'documentCode','location'),
 ('CLIENTE_PRONTUARIO_CANVAS_V1','Cliente - Prontuário (Canvas)','MedicalRecord',174::numeric,110::numeric,'recordNumber','patientName'),
 ('CLIENTE_PROCESSO_CANVAS_V1','Cliente - Processo (Canvas)','Process',100::numeric,70::numeric,'processNumber','documentTitle')
), designs as (
 select s.*,jsonb_build_object(
  'schemaVersion',1,
  'canvas',jsonb_build_object('widthMm',s.width_mm,'heightMm',s.height_mm,'paper','A4','orientation','portrait','gridMm',2,'safeMarginMm',3),
  'elements',jsonb_build_array(
   jsonb_build_object('id','header','type','field','name','Título do cabeçalho','xMm',5,'yMm',4,'widthMm',s.width_mm-40,'heightMm',9,'zIndex',10,'visible',true,'style',jsonb_build_object('fontFamily','Arial','fontSizePt',11,'fontWeight','700','align','center','color','#111111','backgroundColor','transparent','border','none','paddingMm',1,'wrap',true),'binding',jsonb_build_object('field','headerTitle','fallback','ARQUIVO CENTRAL'),'validation',jsonb_build_object('required',true)),
   jsonb_build_object('id','primaryLogo','type','logo','name','Logo principal','xMm',5,'yMm',15,'widthMm',30,'heightMm',14,'zIndex',20,'visible',true,'style',jsonb_build_object('fontFamily','Arial','fontSizePt',7,'fontWeight','400','align','center','color','#555555','backgroundColor','transparent','border','1px solid #999999','paddingMm',1,'wrap',true),'binding',jsonb_build_object('field','primaryLogo'),'validation',jsonb_build_object('required',false,'showOnlyWhenValue',true)),
   jsonb_build_object('id','client','type','field','name','Cliente','xMm',38,'yMm',15,'widthMm',s.width_mm-73,'heightMm',7,'zIndex',11,'visible',true,'style',jsonb_build_object('fontFamily','Arial','fontSizePt',8,'fontWeight','700','align','left','color','#111111','backgroundColor','transparent','border','none','paddingMm',1,'wrap',true),'binding',jsonb_build_object('field','clientName'),'validation',jsonb_build_object('required',false,'showOnlyWhenValue',true)),
   jsonb_build_object('id','contract','type','field','name','Contrato','xMm',38,'yMm',23,'widthMm',s.width_mm-73,'heightMm',7,'zIndex',11,'visible',true,'style',jsonb_build_object('fontFamily','Arial','fontSizePt',7,'fontWeight','400','align','left','color','#111111','backgroundColor','transparent','border','none','paddingMm',1,'wrap',true),'binding',jsonb_build_object('field','contractName'),'validation',jsonb_build_object('required',false,'showOnlyWhenValue',true)),
   jsonb_build_object('id','code','type','field','name','Identificador','xMm',5,'yMm',34,'widthMm',s.width_mm-40,'heightMm',12,'zIndex',11,'visible',true,'style',jsonb_build_object('fontFamily','Arial','fontSizePt',10,'fontWeight','700','align','left','color','#111111','backgroundColor','transparent','border','1px solid #111111','paddingMm',1,'wrap',true),'binding',jsonb_build_object('field',s.primary_field),'validation',jsonb_build_object('required',true)),
   jsonb_build_object('id','detail','type','field','name','Informação complementar','xMm',5,'yMm',48,'widthMm',s.width_mm-40,'heightMm',10,'zIndex',11,'visible',true,'style',jsonb_build_object('fontFamily','Arial','fontSizePt',8,'fontWeight','400','align','left','color','#111111','backgroundColor','transparent','border','1px solid #111111','paddingMm',1,'wrap',true),'binding',jsonb_build_object('field',s.secondary_field),'validation',jsonb_build_object('required',false)),
   jsonb_build_object('id','qr','type','qr','name','QR Code','xMm',s.width_mm-32,'yMm',34,'widthMm',27,'heightMm',27,'zIndex',20,'visible',true,'style',jsonb_build_object('fontFamily','Arial','fontSizePt',7,'fontWeight','400','align','center','color','#111111','backgroundColor','#ffffff','border','none','paddingMm',1,'wrap',false),'binding',jsonb_build_object('field','qrPayload'),'validation',jsonb_build_object('required',true))
  ),
  'bindings',jsonb_build_object('subjectType',s.subject_type,'sampleDataProfile','Genérico')
 ) design_json from seed s
)
insert into ged.label_template_design(id,tenant_id,template_code,template_key,template_name,description,template_kind,print_mode,subject_type,view_name,paper_kind,paper_size,width_mm,height_mm,orientation,status,design_json,current_version,is_system_template,branding_binding_key,client_name_fallback,contract_name_fallback,organization_name_fallback,header_title_fallback,header_subtitle_fallback,label_context,created_at,reg_status)
select gen_random_uuid(),null,d.template_key,d.template_key,d.template_name,'Modelo neutro multi-cliente, editável e associado à identidade visual oficial.','CANVAS','CUSTOM',d.subject_type,'CanvasLabel','A4','A4',d.width_mm,d.height_mm,'portrait','DRAFT',d.design_json,1,false,d.template_key,null,null,null,'ARQUIVO CENTRAL',null,'GENERIC',now(),'ACTIVE'
from designs d
where not exists(select 1 from ged.label_template_design x where x.template_key=d.template_key and x.reg_status in ('A','ACTIVE'));
