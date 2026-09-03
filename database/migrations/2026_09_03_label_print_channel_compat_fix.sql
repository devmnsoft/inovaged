create schema if not exists ged;

alter table if exists ged.label_print
    add column if not exists print_channel varchar(40) not null default 'WEB',
    add column if not exists print_mode varchar(40) null,
    add column if not exists template_version integer null,
    add column if not exists logo_asset_id uuid null,
    add column if not exists logo_brand_name text null,
    add column if not exists logo_width_mm numeric(8,2) null,
    add column if not exists logo_height_mm numeric(8,2) null,
    add column if not exists logo_fit_mode varchar(40) null,
    add column if not exists logo_position varchar(40) null;

alter table if exists ged.label_print_history
    add column if not exists print_channel varchar(40) not null default 'WEB',
    add column if not exists print_mode varchar(40) null,
    add column if not exists template_version integer null,
    add column if not exists logo_asset_id uuid null,
    add column if not exists logo_brand_name text null,
    add column if not exists logo_width_mm numeric(8,2) null,
    add column if not exists logo_height_mm numeric(8,2) null,
    add column if not exists logo_fit_mode varchar(40) null,
    add column if not exists logo_position varchar(40) null;
