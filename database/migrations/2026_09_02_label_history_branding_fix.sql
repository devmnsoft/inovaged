-- Labels Branding Fix RC5: branding used for an audited print (safe to re-run).
alter table if exists ged.label_print_history
 add column if not exists logo_asset_id uuid null,
 add column if not exists logo_brand_name text null,
 add column if not exists logo_width_mm numeric(8,2) null,
 add column if not exists logo_height_mm numeric(8,2) null,
 add column if not exists logo_fit_mode varchar(40) null,
 add column if not exists logo_position varchar(40) null;
