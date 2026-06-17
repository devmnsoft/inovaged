create schema if not exists ged;

alter table if exists ged.loan_request add column if not exists requester_sector_name text null;
alter table if exists ged.loan_request add column if not exists assigned_sector_name text null;
alter table if exists ged.loan_request add column if not exists current_sector_name text null;
alter table if exists ged.loan_request add column if not exists created_by uuid null;

do $$
begin
    if to_regclass('ged.loan_request') is not null then
        update ged.loan_request
        set requester_sector_name = coalesce(requester_sector_name, requester_sector)
        where requester_sector_name is null;
    end if;
end $$;

alter table if exists ged.protocol_request add column if not exists requester_sector_name text null;
alter table if exists ged.protocol_request add column if not exists assigned_sector_name text null;
alter table if exists ged.protocol_request add column if not exists current_sector_name text null;

create index if not exists ix_loan_request_tenant_requester_sector_name
on ged.loan_request(tenant_id, requester_sector_name);

create index if not exists ix_loan_request_tenant_assigned_sector_name
on ged.loan_request(tenant_id, assigned_sector_name);

create index if not exists ix_loan_request_tenant_current_sector_name
on ged.loan_request(tenant_id, current_sector_name);

create index if not exists ix_loan_request_tenant_requester_id
on ged.loan_request(tenant_id, requester_id);

create index if not exists ix_loan_request_tenant_created_by
on ged.loan_request(tenant_id, created_by);

create index if not exists ix_protocol_request_tenant_assigned_sector_name
on ged.protocol_request(tenant_id, assigned_sector_name);

create index if not exists ix_protocol_request_tenant_requester_sector_name
on ged.protocol_request(tenant_id, requester_sector_name);

create index if not exists ix_protocol_request_tenant_requester_user_id
on ged.protocol_request(tenant_id, requester_user_id);
