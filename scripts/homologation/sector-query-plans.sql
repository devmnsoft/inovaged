-- Run only against a disposable database installed with the official migrator.
-- Synthetic workload: one patient, 1,000 encounters, 100,000 documents; rolled back.
\set ON_ERROR_STOP on
begin;
create temp table sector_plan_fixture as select gen_random_uuid() tenant,gen_random_uuid() patient,gen_random_uuid() actor;
insert into ged.tenant(id,name,code) select tenant,'Synthetic query benchmark',tenant::text from sector_plan_fixture;
insert into ged.app_user(id,tenant_id,name,email,password_hash)
select actor,tenant,'Synthetic actor',actor::text||'@example.test','not-a-login-credential' from sector_plan_fixture;
insert into ged.sector_patient(id,tenant_id,source_system,external_code,display_name)
select patient,tenant,'BENCHMARK','000120','Synthetic patient' from sector_plan_fixture;
create temp table sector_plan_encounters as select gen_random_uuid() id,n from generate_series(1,1000)n;
insert into ged.sector_encounter(id,tenant_id,patient_id,external_code)
select e.id,f.tenant,f.patient,lpad(e.n::text,6,'0') from sector_plan_encounters e cross join sector_plan_fixture f;
create temp table sector_plan_documents as
select gen_random_uuid() id,gen_random_uuid() version,e.id encounter,row_number() over() n
from sector_plan_encounters e cross join generate_series(1,100);
insert into ged.document(id,tenant_id,code,title)
select d.id,f.tenant,'BENCH-'||d.n,'Synthetic document' from sector_plan_documents d cross join sector_plan_fixture f;
insert into ged.sector_document_link(id,tenant_id,patient_id,encounter_id,document_id,version_id,catalog_code,
 origin,original_file_name,content_sha256,uploaded_by,transfer_status,preview_status,ocr_status,extraction_status)
select gen_random_uuid(),f.tenant,f.patient,d.encounter,d.id,d.version,'DS23',
 'BENCHMARK','synthetic.pdf',repeat('a',64),f.actor,'NAO_APLICAVEL','NAO_APLICAVEL','NAO_APLICAVEL','NAO_APLICAVEL'
from sector_plan_documents d cross join sector_plan_fixture f;
analyze ged.document;
analyze ged.sector_document_link;
select tenant,patient into temp sector_plan_args from sector_plan_fixture;
select tenant,patient from sector_plan_args \gset
select id encounter from sector_plan_encounters where n=1 \gset
-- Baseline candidate query: visits every encounter of the patient.
explain (analyze,buffers,format text)
select distinct l.document_id from ged.sector_document_link l
where l.tenant_id=:'tenant' and l.patient_id=:'patient' and l.reg_status='A' and l.catalog_code=any(array['DS23']);
-- Current encounter candidate query, as issued by ReadSessionAsync (expand=false).
explain (analyze,buffers,format text)
select distinct l.document_id from ged.sector_document_link l
where l.tenant_id=:'tenant' and l.patient_id=:'patient' and l.reg_status='A'
 and (false or l.encounter_id=:'encounter')
 and (null::uuid is null or l.document_id=null::uuid)
 and (null::uuid is null or l.version_id=null::uuid)
 and (null::text is null or l.catalog_code=null::text)
 and l.catalog_code=any(array['DS23'])
 and exists(select 1 from ged.document d where d.tenant_id=l.tenant_id and d.id=l.document_id and coalesce(d.reg_status,'A')='A');
-- Evaluate a candidate index before deciding whether to add it to a migration.
create index ix_sector_plan_document_active on ged.document(tenant_id,id) where coalesce(reg_status,'A')='A';
explain (analyze,buffers,format text)
select distinct l.document_id from ged.sector_document_link l
where l.tenant_id=:'tenant' and l.patient_id=:'patient' and l.reg_status='A'
 and l.encounter_id=:'encounter' and l.catalog_code=any(array['DS23'])
 and exists(select 1 from ged.document d where d.tenant_id=l.tenant_id and d.id=l.document_id and coalesce(d.reg_status,'A')='A');
-- Equivalent nullable predicate avoids COALESCE selectivity underestimation.
drop index ged.ix_sector_plan_document_active;
explain (analyze,buffers,format text)
select distinct l.document_id from ged.sector_document_link l
where l.tenant_id=:'tenant' and l.patient_id=:'patient' and l.reg_status='A'
 and l.encounter_id=:'encounter' and l.catalog_code=any(array['DS23'])
 and exists(select 1 from ged.document d where d.tenant_id=l.tenant_id and d.id=l.document_id and (d.reg_status='A' or d.reg_status is null));
rollback;
