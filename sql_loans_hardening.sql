-- Hardening módulo de empréstimos
create table if not exists ged.loan_approval_profile (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null,
  profile_name varchar(120) not null,
  role_id uuid not null,
  created_at timestamptz not null default now(),
  reg_status char(1) not null default 'A'
);

create table if not exists ged.loan_request_allowed_file (
  tenant_id uuid not null,
  loan_id uuid not null,
  file_id uuid not null,
  created_at timestamptz not null default now(),
  reg_status char(1) not null default 'A',
  primary key (tenant_id, loan_id, file_id)
);

create index if not exists ix_loan_approval_profile_tenant on ged.loan_approval_profile(tenant_id, reg_status);
create index if not exists ix_loan_allowed_file_loan on ged.loan_request_allowed_file(tenant_id, loan_id, reg_status);

-- limpeza de dados PoC em produção
update ged.audit_log set summary='[REMOVIDO_REFERENCIA_POC]' where upper(coalesce(summary,'')) like '%POC%';
