-- Somente banco descartavel criado pelo roteiro local. Nunca dados reais.
begin;
do $$ begin
  if current_database() !~ '^ai_install_[a-f0-9]{32}$' then raise exception 'Fixture exige banco descartavel ai_install'; end if;
  if not exists(select 1 from ged.app_user where id='bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb001') then raise exception 'Execute primeiro o seed local ficticio'; end if;
end $$;
insert into ged.permission(code,name) values ('Documents.View','Leitura documental'),('GED.DOCUMENTS','Edicao documental'),('Security.Manage','Sigilo documental') on conflict(code) do nothing;
insert into ged.role(id,tenant_id,code,name)
select id,tenant_id,normalized_name,name from ged.app_role where tenant_id='00000000-0000-0000-0000-000000000001' on conflict(id) do nothing;
insert into ged.role_permission(tenant_id,role_id,permission_code)
select r.tenant_id,r.id,p.code from ged.app_role r cross join ged.permission p
where r.tenant_id='00000000-0000-0000-0000-000000000001'
and ((r.normalized_name in ('HOSPITAL','ARQUIVISTAOPHIR','ADMINISTRADOROPHIR') and p.code='Documents.View')
 or (r.normalized_name in ('ARQUIVISTAOPHIR','ADMINISTRADOROPHIR') and p.code='GED.DOCUMENTS')
 or (r.normalized_name='ADMINISTRADOROPHIR' and p.code='Security.Manage')) on conflict do nothing;
insert into ged.tenant(id,name,code) values ('00000000-0000-0000-0000-000000000002','Outra Instituicao Ficticia','fixture-other') on conflict(id) do nothing;
insert into ged.app_user(id,tenant_id,name,email,password_hash,user_name,normalized_email,normalized_user_name)
select v.id::uuid,v.tenant::uuid,v.name,v.email,u.password_hash,v.email,upper(v.email),upper(v.email)
from ged.app_user u cross join (values
('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb006','00000000-0000-0000-0000-000000000001','Sem ACL Ficticio','denied@inovaged.local'),
('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb007','00000000-0000-0000-0000-000000000002','Outro Tenant Ficticio','other@inovaged.local')) v(id,tenant,name,email)
where u.id='bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb001' on conflict(id) do nothing;
insert into ged.user_role(user_id,role_id)
select 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb006',id from ged.app_role where tenant_id='00000000-0000-0000-0000-000000000001' and normalized_name='HOSPITAL' on conflict do nothing;
insert into ged.app_role(id,tenant_id,name,normalized_name) values ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa7','00000000-0000-0000-0000-000000000002','HOSPITAL','HOSPITAL') on conflict(id) do nothing;
insert into ged.role(id,tenant_id,code,name) values ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa7','00000000-0000-0000-0000-000000000002','HOSPITAL','HOSPITAL') on conflict(id) do nothing;
insert into ged.user_role(user_id,role_id) values ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb007','aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa7') on conflict do nothing;
insert into ged.role_permission(tenant_id,role_id,permission_code) values ('00000000-0000-0000-0000-000000000002','aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa7','Documents.View') on conflict do nothing;
insert into ged.document(id,tenant_id,code,title,is_confidential) values ('dddddddd-dddd-dddd-dddd-dddddddddd01','00000000-0000-0000-0000-000000000001','FIXTURE-AI-01','Documento ficticio inicial',false) on conflict(id) do nothing;
insert into ged.document_version(id,tenant_id,document_id,version_number,file_name,file_extension,file_size_bytes,storage_path,content_type)
values ('eeeeeeee-eeee-eeee-eeee-eeeeeeeeee01','00000000-0000-0000-0000-000000000001','dddddddd-dddd-dddd-dddd-dddddddddd01',1,'fixture.txt','.txt',90,'fixture.txt','text/plain') on conflict(id) do nothing;
update ged.document set current_version_id='eeeeeeee-eeee-eeee-eeee-eeeeeeeeee01' where id='dddddddd-dddd-dddd-dddd-dddddddddd01';
insert into ged.document_search(tenant_id,document_id,version_id,title,ocr_text,search_vector)
values ('00000000-0000-0000-0000-000000000001','dddddddd-dddd-dddd-dddd-dddddddddd01','eeeeeeee-eeee-eeee-eeee-eeeeeeeeee01','Documento ficticio',E'TITULO: Documento de homologacao\nDESCRICAO: Texto inventado para testes locais.\nSIGILOSO\nCLASSE: FIX-AI',to_tsvector('simple','Documento ficticio')) on conflict do nothing;
insert into ged.document_acl(document_id,user_id,can_read,can_write)
select 'dddddddd-dddd-dddd-dddd-dddddddddd01',u.id,true,u.id<>'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb005'::uuid
from ged.app_user u where u.id in ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb003','bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb004','bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb005')
and not exists(select 1 from ged.document_acl a where a.document_id='dddddddd-dddd-dddd-dddd-dddddddddd01' and a.user_id=u.id);
insert into ged.ai_tenant_policy(tenant_id,enabled,allowed_tasks,allowed_providers,task_models,monthly_token_limit)
values ('00000000-0000-0000-0000-000000000001',true,'["Summarize","ExtractMetadata","SuggestClassification","SuggestArchivalClassification"]','["Deterministic"]','{"Summarize":"deterministic-v1","ExtractMetadata":"deterministic-v1","SuggestClassification":"deterministic-v1","SuggestArchivalClassification":"deterministic-v1"}',1000000) on conflict(tenant_id) do nothing;
insert into ged.classification_plan_version(id,tenant_id,version_no,title)
values ('cccccccc-cccc-cccc-cccc-cccccccccc02','00000000-0000-0000-0000-000000000001',1,'Plano ficticio local') on conflict(id) do nothing;
insert into ged.classification_plan(id,tenant_id,code,name,title,retention_active_days,final_destination)
values ('cccccccc-cccc-cccc-cccc-cccccccccc01','00000000-0000-0000-0000-000000000001','FIX-AI','Classe ficticia','Classe ficticia',365,'REAVALIAR') on conflict(id) do nothing;
insert into ged.classification_plan_version_item(tenant_id,version_id,classification_id,code,name,retention_start_event,retention_active_days,retention_active_months,retention_active_years,retention_archive_days,retention_archive_months,retention_archive_years,final_destination,requires_digital_signature,is_confidential,is_active)
select '00000000-0000-0000-0000-000000000001','cccccccc-cccc-cccc-cccc-cccccccccc02','cccccccc-cccc-cccc-cccc-cccccccccc01','FIX-AI','Classe ficticia','INCLUSAO',365,0,0,0,0,0,'REAVALIAR',false,false,true
where not exists(select 1 from ged.classification_plan_version_item where version_id='cccccccc-cccc-cccc-cccc-cccccccccc02' and classification_id='cccccccc-cccc-cccc-cccc-cccccccccc01');
commit;
