-- Isola cada abertura e registra a permissão de navegar entre atendimentos.
-- Não reescreve seed nem personalização do catálogo.

alter table ged.sector_integration add column if not exists allow_encounter_switch boolean not null default false;

alter table ged.sector_open_session add column if not exists public_id uuid;
alter table ged.sector_open_session add column if not exists allow_encounter_switch boolean not null default false;
alter table ged.sector_open_session add column if not exists scope_kind text not null default 'atendimento';

update ged.sector_open_session set public_id = gen_random_uuid() where public_id is null;
alter table ged.sector_open_session alter column public_id set not null;
create unique index if not exists ux_sector_open_session_public on ged.sector_open_session (public_id);

alter table ged.sector_open_code add column if not exists allow_encounter_switch boolean not null default false;
alter table ged.sector_open_code add column if not exists scope_kind text not null default 'atendimento';

alter table ged.sector_open_session add column if not exists protection_hash char(64);

alter table ged.sector_extracted_field add column if not exists source_kind text not null default 'TEXTO_VERSIONADO';
alter table ged.sector_extracted_field add column if not exists mechanism text not null default 'REGRAS_LOCAIS';
alter table ged.sector_extracted_field add column if not exists confidence numeric;
