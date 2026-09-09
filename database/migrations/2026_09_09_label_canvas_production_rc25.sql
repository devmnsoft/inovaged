-- RC25 - Label Canvas Production Studio. Idempotente; não altera templates clássicos.
create schema if not exists ged;

update ged.label_template_design
   set design_json=jsonb_set(design_json,'{schemaVersion}','2'::jsonb,true),updated_at=coalesce(updated_at,now())
 where template_key in ('CLIENTE_CAIXA_CANVAS_V1','CLIENTE_DOCUMENTO_CANVAS_V1','CLIENTE_PASTA_CANVAS_V1','CLIENTE_PRONTUARIO_CANVAS_V1','CLIENTE_PROCESSO_CANVAS_V1')
   and coalesce((design_json->>'schemaVersion')::integer,1)<2
   and reg_status in ('A','ACTIVE');

with candidates as (
 select d.*
   from ged.label_template_design d
  where d.template_key in ('CLIENTE_CAIXA_CANVAS_V1','CLIENTE_DOCUMENTO_CANVAS_V1','CLIENTE_PASTA_CANVAS_V1','CLIENTE_PRONTUARIO_CANVAS_V1','CLIENTE_PROCESSO_CANVAS_V1')
    and d.reg_status in ('A','ACTIVE')
    and not exists(select 1 from ged.label_template_design_version v where v.template_design_id=d.id and v.status='PUBLISHED' and v.reg_status in ('A','ACTIVE'))
), inserted as (
 insert into ged.label_template_design_version(id,tenant_id,template_design_id,version_no,version_number,status,design_json,snapshot_json,change_summary,notes,created_by,created_at,published_by,published_at,snapshot_hash,reg_status)
 select gen_random_uuid(),d.tenant_id,d.id,1,1,'PUBLISHED',d.design_json,
        jsonb_build_object('templateKey',d.template_key,'templateName',d.template_name,'subjectType',d.subject_type,'paperKind',d.paper_kind,'widthMm',d.width_mm,'heightMm',d.height_mm,'orientation',d.orientation,'defaultBrandingProfileId',d.default_branding_profile_id,'brandingBindingKey',d.branding_binding_key,'fallbacks',jsonb_build_object('clientName',d.client_name_fallback,'contractName',d.contract_name_fallback,'organizationName',d.organization_name_fallback,'headerTitle',d.header_title_fallback,'headerSubtitle',d.header_subtitle_fallback),'designJson',d.design_json),
        'Publicação inicial RC25.','Publicação inicial RC25.',d.created_by,now(),d.created_by,now(),encode(digest(d.design_json::text,'sha256'),'hex'),'ACTIVE'
   from candidates d
 returning template_design_id
)
update ged.label_template_design d
   set status='PUBLISHED',current_version=1,published_at=coalesce(published_at,now()),updated_at=now()
 where d.id in (select template_design_id from inserted);

