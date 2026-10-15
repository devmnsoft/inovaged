-- Bind pre-login contexts to their original integration configuration.
-- Legacy temporary tickets lack this provenance and require a new entry after upgrade.
alter table ged.sector_entry_ticket add column if not exists integration_fingerprint char(64);
alter table ged.sector_integration add column if not exists display_name text;
create index if not exists ix_sector_open_request_tenant_correlation on ged.sector_open_request(tenant_id,correlation_id);
