-- Catálogo idempotente do modelo oficial LocDesk Pasta HOL.
-- O bloco não cria tabelas: instalações sem catálogo continuam usando o MinimumCatalog da aplicação.
do $$
begin
  if to_regclass('ged.label_template') is not null then
    insert into ged.label_template(id,tenant_id,code,name,description,print_mode,subject_type,view_name,version,is_system_template,is_custom_template,is_active,is_default,supports_batch,allows_manual_fields,display_order,reg_status)
    select gen_random_uuid(),null,'LOCDESK_PASTA_HOL_V1','LocDesk - Pasta HOL','Modelo LocDesk para pasta/documento do Hospital Ophir Loyola.','CUSTOM','DOCUMENT','LocDeskFolderHolLabel',1,false,true,true,true,true,true,45,'A'
    where not exists(select 1 from ged.label_template where code='LOCDESK_PASTA_HOL_V1' and tenant_id is null and coalesce(reg_status,'A')='A');
  elsif to_regclass('ged.label_template_catalog') is not null then
    insert into ged.label_template_catalog(code,name,print_mode,subject_type,view_name,version,description,supports_batch,allows_manual_fields,is_system_template,is_active,display_order)
    values('LOCDESK_PASTA_HOL_V1','LocDesk - Pasta HOL','CUSTOM','DOCUMENT','LocDeskFolderHolLabel','1','Modelo LocDesk para pasta/documento do Hospital Ophir Loyola.',true,true,false,true,45)
    on conflict(code) do update set name=excluded.name,print_mode=excluded.print_mode,subject_type=excluded.subject_type,view_name=excluded.view_name,description=excluded.description,is_active=true;
  end if;
end $$;
