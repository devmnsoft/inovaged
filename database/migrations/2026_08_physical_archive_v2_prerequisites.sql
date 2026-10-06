-- Bridge existing physical archive tables before the published v2 indexes.
-- Existing identities, box numbers, inventory observations and timestamps remain intact.
alter table if exists ged.physical_box add column if not exists reg_status char(1) not null default 'A';
alter table if exists ged.physical_box add column if not exists status varchar(40) not null default 'ACTIVE';
alter table if exists ged.physical_box add column if not exists box_code text;
alter table if exists ged.physical_inventory_session add column if not exists reg_status char(1) not null default 'A';
alter table if exists ged.physical_inventory_session add column if not exists started_at timestamptz;
