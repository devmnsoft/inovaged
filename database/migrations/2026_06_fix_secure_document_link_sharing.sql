create schema if not exists ged;

create table if not exists ged.secure_document_link (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    loan_request_id uuid null,
    document_id uuid not null,
    version_id uuid null,
    token_hash text not null,
    public_url text null,
    title text null,
    description text null,
    recipient_name text null,
    recipient_contact text null,
    is_permanent boolean not null default false,
    expires_at timestamptz null,
    max_access_count int null,
    access_count int not null default 0,
    allow_preview boolean not null default true,
    allow_download boolean not null default false,
    allow_smart_search boolean not null default true,
    created_by uuid null,
    created_at timestamptz not null default now(),
    last_access_at timestamptz null,
    revoked_at timestamptz null,
    revoked_by uuid null,
    revoke_reason text null,
    reg_status char(1) not null default 'A'
);

alter table ged.secure_document_link add column if not exists public_url text null;
alter table ged.secure_document_link add column if not exists title text null;
alter table ged.secure_document_link add column if not exists description text null;
alter table ged.secure_document_link add column if not exists recipient_name text null;
alter table ged.secure_document_link add column if not exists recipient_contact text null;
alter table ged.secure_document_link add column if not exists allow_preview boolean not null default true;
alter table ged.secure_document_link add column if not exists allow_download boolean not null default false;
alter table ged.secure_document_link add column if not exists allow_smart_search boolean not null default true;
alter table ged.secure_document_link add column if not exists is_permanent boolean not null default false;
alter table ged.secure_document_link add column if not exists expires_at timestamptz null;
alter table ged.secure_document_link add column if not exists max_access_count int null;
alter table ged.secure_document_link add column if not exists access_count int not null default 0;
alter table ged.secure_document_link add column if not exists last_access_at timestamptz null;
alter table ged.secure_document_link add column if not exists revoked_at timestamptz null;
alter table ged.secure_document_link add column if not exists revoked_by uuid null;
alter table ged.secure_document_link add column if not exists revoke_reason text null;
alter table ged.secure_document_link add column if not exists reg_status char(1) not null default 'A';

create table if not exists ged.secure_document_link_access (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    secure_link_id uuid not null,
    accessed_at timestamptz not null default now(),
    ip_address text null,
    user_agent text null,
    success boolean not null default true,
    reason text null
);

create unique index if not exists ux_secure_document_link_token_hash
on ged.secure_document_link(token_hash)
where coalesce(reg_status,'A')='A';

create index if not exists ix_secure_document_link_document
on ged.secure_document_link(tenant_id, document_id, created_at desc);

create index if not exists ix_secure_document_link_loan
on ged.secure_document_link(tenant_id, loan_request_id, created_at desc);

create index if not exists ix_secure_document_link_access_link
on ged.secure_document_link_access(tenant_id, secure_link_id, accessed_at desc);
