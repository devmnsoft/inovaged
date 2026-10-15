-- Explicit embedding origin and bounded, authenticated recovery. No plaintext grants.
alter table ged.sector_entry_ticket add column if not exists origin text not null default '';
alter table ged.sector_entry_ticket add column if not exists destination text not null default 'pagina';
alter table ged.sector_open_request add column if not exists request_fingerprint char(64);
alter table ged.sector_open_request add column if not exists recovery_count integer not null default 0;
-- Existing requests retain a null fingerprint and cannot be compared or recovered as new requests.
create index if not exists ix_sector_open_session_correlation on ged.sector_open_session(integration_id,correlation_id);
create index if not exists ix_sector_open_code_correlation on ged.sector_open_code(integration_id,correlation_id);
