-- Durable handoff to the existing GED queue. No clinical text is copied to jobs.
alter table ged.processing_job drop constraint if exists ck_processing_job_type;
alter table ged.processing_job add constraint ck_processing_job_type
 check (job_type in ('PREVIEW','OCR','SMART_INDEX','QUALITY','CLASSIFICATION','SECTOR_EXTRACT'));

create table if not exists ged.sector_processing_state (
 tenant_id uuid not null,
 version_id uuid not null,
 requested_at timestamptz not null default now(),
 completed_at timestamptz,
 input_hash text,
 primary key(tenant_id,version_id)
);

create or replace function ged.sector_request_extraction(p_tenant uuid,p_version uuid)
returns void language plpgsql as $$
begin
 if not exists(select 1 from ged.sector_document_link where tenant_id=p_tenant and version_id=p_version and reg_status='A') then return; end if;
 -- This row lock serializes notifications with processing and queue completion.
 insert into ged.sector_processing_state(tenant_id,version_id) values(p_tenant,p_version)
 on conflict(tenant_id,version_id) do update set requested_at=clock_timestamp();
 insert into ged.processing_job(tenant_id,document_id,document_version_id,job_type,priority)
 select p_tenant,document_id,p_version,'SECTOR_EXTRACT',6 from ged.sector_document_link
 where tenant_id=p_tenant and version_id=p_version and reg_status='A'
 on conflict do nothing;
end $$;

create or replace function ged.sector_processing_notification()
returns trigger language plpgsql as $$
begin
 if TG_TABLE_NAME in ('sector_document_link','document_search') then
  perform ged.sector_request_extraction(NEW.tenant_id,NEW.version_id);
 elsif TG_TABLE_NAME='document_version' then
  perform ged.sector_request_extraction(NEW.tenant_id,NEW.id);
 else
  perform ged.sector_request_extraction(NEW.tenant_id,NEW.document_version_id);
 end if;
 return NEW;
end $$;

drop trigger if exists sector_processing_link on ged.sector_document_link;
create trigger sector_processing_link after insert or update of version_id,catalog_code,reg_status on ged.sector_document_link
 for each row execute function ged.sector_processing_notification();
drop trigger if exists sector_processing_text on ged.document_version;
create trigger sector_processing_text after update of content_text,ocr_text on ged.document_version
 for each row when (OLD.content_text is distinct from NEW.content_text or OLD.ocr_text is distinct from NEW.ocr_text)
 execute function ged.sector_processing_notification();
drop trigger if exists sector_processing_search on ged.document_search;
create trigger sector_processing_search after insert or update of ocr_text on ged.document_search
 for each row execute function ged.sector_processing_notification();
drop trigger if exists sector_processing_ocr on ged.ocr_job;
create trigger sector_processing_ocr after insert or update of status on ged.ocr_job
 for each row execute function ged.sector_processing_notification();
drop trigger if exists sector_processing_preview on ged.preview_status;
create trigger sector_processing_preview after insert or update of status on ged.preview_status
 for each row execute function ged.sector_processing_notification();

select ged.sector_request_extraction(tenant_id,version_id)
from ged.sector_document_link where reg_status='A';
