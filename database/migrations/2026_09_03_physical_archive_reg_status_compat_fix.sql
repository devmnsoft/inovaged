-- RC15: compatibility for physical archive installations created before lifecycle columns.
create schema if not exists ged;
alter table if exists ged.physical_location add column if not exists reg_status char(1) not null default 'A';
alter table if exists ged.physical_box add column if not exists reg_status char(1) not null default 'A';
alter table if exists ged.physical_box_document add column if not exists reg_status char(1) not null default 'A';
alter table if exists ged.physical_movement add column if not exists reg_status char(1) not null default 'A';
alter table if exists ged.physical_inventory_session add column if not exists reg_status char(1) not null default 'A';
alter table if exists ged.physical_inventory_item add column if not exists reg_status char(1) not null default 'A';
alter table if exists ged.physical_loan add column if not exists reg_status char(1) not null default 'A';
alter table if exists ged.physical_custody_event add column if not exists reg_status char(1) not null default 'A';
