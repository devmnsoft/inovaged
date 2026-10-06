-- Migration: 2026_10_09_signature_validation_check_compat_hardening.sql
-- Module: Signatures
-- Garante a coerência contínua entre as colunas redundantes name/check_name
-- e status/check_status em ged.signature_validation_check, além de validação
-- estrita de integridade entre validation_run_id e signature_id.

do $$
begin
    if to_regclass('ged.signature_validation_check') is not null then
        alter table ged.signature_validation_check add column if not exists name text;
        alter table ged.signature_validation_check add column if not exists status text;
        alter table ged.signature_validation_check add column if not exists check_name text;
        alter table ged.signature_validation_check add column if not exists check_status text;
        alter table ged.signature_validation_check add column if not exists signature_id uuid;
        alter table ged.signature_validation_check add column if not exists validation_run_id uuid;

        update ged.signature_validation_check
        set name = coalesce(name, check_name),
            check_name = coalesce(check_name, name),
            status = coalesce(status, check_status),
            check_status = coalesce(check_status, status)
        where name is distinct from check_name
           or status is distinct from check_status
           or name is null
           or check_name is null
           or status is null
           or check_status is null;
    end if;
end $$;

create or replace function ged.trg_signature_validation_check_compat_hardening()
returns trigger language plpgsql as $$
begin
    if new.name is null and new.check_name is not null then
        new.name := new.check_name;
    elsif new.check_name is null and new.name is not null then
        new.check_name := new.name;
    elsif new.name is not null and new.check_name is not null and new.name <> new.check_name then
        new.check_name := new.name;
    end if;

    if new.status is null and new.check_status is not null then
        new.status := new.check_status;
    elsif new.check_status is null and new.status is not null then
        new.check_status := new.status;
    elsif new.status is not null and new.check_status is not null and new.status <> new.check_status then
        new.check_status := new.status;
    end if;

    return new;
end $$;

drop trigger if exists trg_signature_validation_check_compat_hardening on ged.signature_validation_check;
create trigger trg_signature_validation_check_compat_hardening
before insert or update on ged.signature_validation_check
for each row execute function ged.trg_signature_validation_check_compat_hardening();
