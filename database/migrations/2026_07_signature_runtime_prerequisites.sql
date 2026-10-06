-- Bridge the run-based consumer contract and older signature_id indexes.
-- No signature or validation result is invented.
alter table if exists ged.document_signature add column if not exists created_at timestamptz;
do $$
begin
    if exists(select 1 from information_schema.columns where table_schema='ged'
        and table_name='document_signature' and column_name='signed_at') then
        update ged.document_signature set created_at=signed_at where created_at is null;
    end if;
end $$;
alter table if exists ged.document_signature alter column created_at set default now();
create table if not exists ged.signature_validation_check (
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null,
    validation_run_id uuid, name text, status text, message text,
    evidence_hash text, check_order integer not null default 0,
    created_at timestamptz not null default now()
);
alter table ged.signature_validation_check add column if not exists signature_id uuid;
alter table ged.signature_validation_check add column if not exists validation_run_id uuid;
alter table ged.signature_validation_check add column if not exists name text;
alter table ged.signature_validation_check add column if not exists status text;
alter table ged.signature_validation_check add column if not exists check_name text;
alter table ged.signature_validation_check add column if not exists check_status text;
alter table ged.signature_validation_check alter column signature_id drop not null;
alter table ged.signature_validation_check alter column check_name drop not null;
alter table ged.signature_validation_check alter column check_status drop not null;
update ged.signature_validation_check set name=coalesce(name,check_name), status=coalesce(status,check_status);

create table if not exists ged.signature_certificate_chain (
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null,
    validation_run_id uuid, chain_order integer not null, certificate_der bytea not null,
    certificate_sha256 text, created_at timestamptz not null default now()
);
alter table ged.signature_certificate_chain add column if not exists signature_id uuid;
alter table ged.signature_certificate_chain add column if not exists validation_run_id uuid;
alter table ged.signature_certificate_chain alter column signature_id drop not null;

create or replace function ged.signature_child_run_identity() returns trigger language plpgsql as $$
declare parent_signature uuid;
begin
    if new.validation_run_id is not null then
        select signature_id into parent_signature from ged.signature_validation_run
        where tenant_id=new.tenant_id and id=new.validation_run_id;
        if parent_signature is null or (new.signature_id is not null and new.signature_id <> parent_signature) then
            raise exception 'signature_run_identity_invalid' using errcode='23503';
        end if;
        new.signature_id := parent_signature;
    end if;
    return new;
end $$;
drop trigger if exists signature_check_run_identity on ged.signature_validation_check;
create trigger signature_check_run_identity before insert or update on ged.signature_validation_check
for each row execute function ged.signature_child_run_identity();
drop trigger if exists signature_chain_run_identity on ged.signature_certificate_chain;
create trigger signature_chain_run_identity before insert or update on ged.signature_certificate_chain
for each row execute function ged.signature_child_run_identity();

do $$
begin
    if to_regclass('ged.signature_validation_run') is not null then
        update ged.signature_validation_check c set signature_id=r.signature_id
        from ged.signature_validation_run r
        where r.tenant_id=c.tenant_id and r.id=c.validation_run_id and c.signature_id is null;
        update ged.signature_certificate_chain c set signature_id=r.signature_id
        from ged.signature_validation_run r
        where r.tenant_id=c.tenant_id and r.id=c.validation_run_id and c.signature_id is null;
    end if;
end $$;
