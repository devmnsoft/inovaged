-- Legacy Schema Compatibility Sweep - Administração e Etiquetas.
-- Somente evolução aditiva; seguro para execução repetida e bancos parcialmente migrados.
create schema if not exists ged;

do $$
begin
    if to_regclass('ged.permission') is not null then
        alter table ged.permission add column if not exists description text;
        alter table ged.permission add column if not exists module text;
    end if;
    if to_regclass('ged.label_template') is not null then
        alter table ged.label_template add column if not exists description text;
        alter table ged.label_template add column if not exists subject_type text;
        alter table ged.label_template add column if not exists template_code text;
        alter table ged.label_template add column if not exists reg_status char(1) default 'A';
    end if;
    if to_regclass('ged.label_print') is not null then
        alter table ged.label_print add column if not exists template_code text;
        alter table ged.label_print add column if not exists subject_type text;
        alter table ged.label_print add column if not exists created_at timestamptz default now();
        alter table ged.label_print add column if not exists reg_status char(1) default 'A';
    end if;
    if to_regclass('ged.locdesk_label_draft') is not null then
        alter table ged.locdesk_label_draft add column if not exists label_kind text;
        alter table ged.locdesk_label_draft add column if not exists payload_json jsonb;
        alter table ged.locdesk_label_draft add column if not exists created_at timestamptz default now();
        alter table ged.locdesk_label_draft add column if not exists reg_status char(1) default 'A';
    end if;
end $$;
