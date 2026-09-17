-- Compatibilidade: instalações com ged.physical_* criadas sem as colunas de ciclo de vida
-- usadas pelo dashboard /Physical/Dashboard (erro 42703: coluna "status" não existe).
create schema if not exists ged;

alter table if exists ged.physical_box
    add column if not exists box_code text,
    add column if not exists label_code text,
    add column if not exists title text,
    add column if not exists location_id uuid,
    add column if not exists status varchar(40) not null default 'ACTIVE',
    add column if not exists current_holder text,
    add column if not exists updated_at timestamptz,
    add column if not exists reg_status char(1) not null default 'A';

alter table if exists ged.physical_inventory_session
    add column if not exists session_number text,
    add column if not exists title text,
    add column if not exists location_id uuid,
    add column if not exists status varchar(40) not null default 'OPEN',
    add column if not exists started_by uuid,
    add column if not exists started_at timestamptz not null default now(),
    add column if not exists closed_by uuid,
    add column if not exists closed_at timestamptz,
    add column if not exists notes text,
    add column if not exists reg_status char(1) not null default 'A';

alter table if exists ged.physical_inventory_item
    add column if not exists result varchar(40) not null default 'PENDING',
    add column if not exists scanned_code text,
    add column if not exists scanned_at timestamptz,
    add column if not exists notes text,
    add column if not exists reg_status char(1) not null default 'A';

alter table if exists ged.physical_loan
    add column if not exists loan_number text,
    add column if not exists box_id uuid,
    add column if not exists requested_by_name text,
    add column if not exists requested_by_department text,
    add column if not exists reason text,
    add column if not exists status varchar(40) not null default 'OPEN',
    add column if not exists loaned_by uuid,
    add column if not exists loaned_at timestamptz not null default now(),
    add column if not exists due_at timestamptz,
    add column if not exists returned_by uuid,
    add column if not exists returned_at timestamptz,
    add column if not exists return_notes text,
    add column if not exists reg_status char(1) not null default 'A';

update ged.physical_box
   set status = coalesce(nullif(status, ''), 'ACTIVE')
 where status is null or btrim(status) = '';

update ged.physical_inventory_session
   set status = coalesce(nullif(status, ''), 'OPEN')
 where status is null or btrim(status) = '';

update ged.physical_loan
   set status = coalesce(nullif(status, ''), 'OPEN')
 where status is null or btrim(status) = '';

update ged.physical_inventory_session
   set session_number = coalesce(nullif(session_number, ''), 'INV-' || substr(id::text, 1, 8))
 where session_number is null or btrim(session_number) = '';

update ged.physical_inventory_session
   set title = coalesce(nullif(title, ''), 'Inventário')
 where title is null or btrim(title) = '';

update ged.physical_loan
   set loan_number = coalesce(nullif(loan_number, ''), 'EMP-' || substr(id::text, 1, 8))
 where loan_number is null or btrim(loan_number) = '';
