-- Smart contextual search, guided loans and secure shared delivery
create schema if not exists ged;
create extension if not exists pgcrypto;
-- unaccent is optional; application falls back to lower/ILIKE when unavailable.

create table if not exists ged.search_context_term (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    term text not null,
    normalized_term text not null,
    category text not null,
    synonyms text[] null,
    related_terms text[] null,
    weight numeric not null default 1,
    is_sensitive boolean not null default false,
    created_at timestamptz not null default now(),
    reg_status char(1) not null default 'A'
);
create index if not exists ix_search_context_term_tenant_normalized on ged.search_context_term(tenant_id, normalized_term);
create index if not exists ix_search_context_term_tenant_category on ged.search_context_term(tenant_id, category);
create index if not exists ix_search_context_term_synonyms on ged.search_context_term using gin(synonyms);
create index if not exists ix_search_context_term_related_terms on ged.search_context_term using gin(related_terms);

insert into ged.search_context_term(tenant_id, term, normalized_term, category, synonyms, related_terms, weight, is_sensitive)
select t.id, s.term, lower(coalesce(s.normalized_term,s.term,'')), s.category, s.synonyms, s.related_terms, s.weight, s.is_sensitive
from (select distinct tenant_id as id from ged.document where tenant_id is not null union select distinct tenant_id from ged.app_user where tenant_id is not null union select '00000000-0000-0000-0000-000000000000'::uuid) t
cross join (values
('câncer de mama','cancer de mama','clinical',array['neoplasia mamária','carcinoma mamário','tumor de mama','CA mama','câncer mamário','cancer de mama','oncologia mama','mastologia'],array['mama','biópsia','quimioterapia','radioterapia','oncologia','mamografia'],3,true),
('diabetes','diabetes','clinical',array['diabete','DM','diabetes mellitus'],array['endocrinologia','glicemia','insulina'],2,true),
('AVC','avc','clinical',array['acidente vascular cerebral','derrame'],array['neurologia','tomografia','prontuário'],2,true),
('APAC','apac','administrative',array['autorização de procedimento','autorização de alta complexidade'],array['guia','autorização','oncologia'],2,false),
('ultrassom','ultrassom','exam',array['ultrassonografia','USG'],array['exame','laudo'],1.5,false),
('tomografia','tomografia','exam',array['TC','tomografia computadorizada'],array['exame','laudo'],1.5,false),
('prontuário','prontuario','document_type',array['registro do paciente','ficha do paciente'],array['paciente','histórico clínico'],1.5,true)
) as s(term, normalized_term, category, synonyms, related_terms, weight, is_sensitive)
where not exists (select 1 from ged.search_context_term x where x.tenant_id=t.id and x.normalized_term=lower(s.normalized_term) and x.reg_status='A');


do $$
begin
    if exists (select 1 from pg_type t join pg_namespace n on n.oid=t.typnamespace where n.nspname='ged' and t.typname='loan_status') then
        alter type ged.loan_status add value if not exists 'DRAFT';
        alter type ged.loan_status add value if not exists 'TRIAGE';
        alter type ged.loan_status add value if not exists 'NEEDS_INFO';
        alter type ged.loan_status add value if not exists 'PREPARING_PHYSICAL';
        alter type ged.loan_status add value if not exists 'WAITING_PICKUP';
        alter type ged.loan_status add value if not exists 'DIGITAL_LINK_SENT';
    end if;
end $$;

alter table if exists ged.loan_request add column if not exists request_no text null;
alter table if exists ged.loan_request add column if not exists request_type text not null default 'DOCUMENT_REQUEST';
alter table if exists ged.loan_request add column if not exists delivery_mode text not null default 'PHYSICAL';
alter table if exists ged.loan_request add column if not exists request_description text null;
alter table if exists ged.loan_request add column if not exists patient_name text null;
alter table if exists ged.loan_request add column if not exists medical_record_number text null;
alter table if exists ged.loan_request add column if not exists patient_identifier_masked text null;
alter table if exists ged.loan_request add column if not exists desired_due_at timestamptz null;
alter table if exists ged.loan_request add column if not exists sla_due_at timestamptz null;
alter table if exists ged.loan_request add column if not exists sla_hours int null;
alter table if exists ged.loan_request add column if not exists priority text not null default 'NORMAL';
alter table if exists ged.loan_request add column if not exists requester_contact text null;
alter table if exists ged.loan_request add column if not exists requester_sector_id uuid null;
alter table if exists ged.loan_request add column if not exists requester_sector_name text null;
alter table if exists ged.loan_request add column if not exists admin_response text null;
alter table if exists ged.loan_request add column if not exists admin_response_at timestamptz null;
alter table if exists ged.loan_request add column if not exists admin_response_by uuid null;
alter table if exists ged.loan_request add column if not exists delivery_instructions text null;
alter table if exists ged.loan_request add column if not exists digital_delivery_enabled boolean not null default false;
alter table if exists ged.loan_request add column if not exists physical_delivery_enabled boolean not null default false;
alter table if exists ged.loan_request add column if not exists secure_link_id uuid null;
alter table if exists ged.loan_request add column if not exists status_detail text null;
alter table if exists ged.loan_request add column if not exists last_message_at timestamptz null;
alter table if exists ged.loan_request add column if not exists reg_status char(1) not null default 'A';

alter table if exists ged.loan_request_item add column if not exists is_manual boolean not null default false;
alter table if exists ged.loan_request_item add column if not exists requested_text text null;
alter table if exists ged.loan_request_item add column if not exists date_hint text null;
alter table if exists ged.loan_request_item add column if not exists context_terms text[] null;
alter table if exists ged.loan_request_item add column if not exists document_type text null;
alter table if exists ged.loan_request_item add column if not exists patient_name text null;
alter table if exists ged.loan_request_item add column if not exists medical_record_number text null;
alter table if exists ged.loan_request_item add column if not exists matched_document_id uuid null;
alter table if exists ged.loan_request_item add column if not exists matched_version_id uuid null;
alter table if exists ged.loan_request_item add column if not exists match_score numeric null;
alter table if exists ged.loan_request_item add column if not exists match_reason text null;
alter table if exists ged.loan_request_item add column if not exists digital_available boolean not null default false;
alter table if exists ged.loan_request_item add column if not exists physical_available boolean not null default false;
alter table if exists ged.loan_request_item add column if not exists reg_status char(1) not null default 'A';

create table if not exists ged.loan_request_message (
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null, loan_request_id uuid not null,
    sender_user_id uuid null, sender_name text null, sender_role text null, message text not null,
    message_type text not null default 'COMMENT', is_internal boolean not null default false,
    created_at timestamptz not null default now(), reg_status char(1) not null default 'A');
create index if not exists ix_loan_request_message_request on ged.loan_request_message(tenant_id, loan_request_id, created_at desc);

create table if not exists ged.loan_sla_policy (
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null, code text not null, name text not null,
    delivery_mode text not null, priority text not null default 'NORMAL', sla_hours int not null,
    is_default boolean not null default false, created_at timestamptz not null default now(), reg_status char(1) not null default 'A');
create unique index if not exists ux_loan_sla_policy_tenant_code on ged.loan_sla_policy(tenant_id, code) where reg_status='A';
insert into ged.loan_sla_policy(tenant_id, code, name, delivery_mode, priority, sla_hours, is_default)
select t.id, v.code, v.name, v.delivery_mode, v.priority, v.sla_hours, v.is_default from (select distinct tenant_id as id from ged.app_user where tenant_id is not null) t cross join (values
('PHYSICAL_NORMAL','Físico normal','PHYSICAL','NORMAL',48,true),('PHYSICAL_URGENT','Físico urgente','PHYSICAL','URGENT',24,false),('DIGITAL_NORMAL','Digital normal','DIGITAL','NORMAL',24,true),('DIGITAL_URGENT','Digital urgente','DIGITAL','URGENT',8,false)) v(code,name,delivery_mode,priority,sla_hours,is_default)
on conflict do nothing;

create table if not exists ged.secure_document_link (
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null, loan_request_id uuid null, document_id uuid not null,
    version_id uuid null, token_hash text not null, expires_at timestamptz not null, max_access_count int not null default 5,
    access_count int not null default 0, allow_smart_search boolean not null default true, allow_download boolean not null default false,
    created_by uuid null, created_at timestamptz not null default now(), revoked_at timestamptz null, revoked_by uuid null,
    revoke_reason text null, reg_status char(1) not null default 'A');
create unique index if not exists ux_secure_document_link_token_hash on ged.secure_document_link(token_hash);
create index if not exists ix_secure_document_link_loan on ged.secure_document_link(tenant_id, loan_request_id);

create table if not exists ged.secure_document_link_access (
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null, secure_link_id uuid not null,
    accessed_at timestamptz not null default now(), ip_address text null, user_agent text null, success boolean not null, reason text null);
create index if not exists ix_secure_document_link_access_link on ged.secure_document_link_access(tenant_id, secure_link_id, accessed_at desc);
