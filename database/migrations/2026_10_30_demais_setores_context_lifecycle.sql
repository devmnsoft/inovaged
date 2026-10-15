-- Bounded admission, expiration and batched housekeeping of temporary contexts.
-- No clinical records, canonical permissions or archival retention rules are changed.
create index if not exists ix_sector_entry_ticket_integration_created
    on ged.sector_entry_ticket(integration_id,created_at);
create index if not exists ix_sector_entry_ticket_pending
    on ged.sector_entry_ticket(integration_id,expires_at) where consumed_at is null;
create index if not exists ix_sector_entry_ticket_expiry
    on ged.sector_entry_ticket(expires_at,id);
create index if not exists ix_sector_open_code_expiry on ged.sector_open_code(expires_at,id);
create index if not exists ix_sector_open_session_expiry on ged.sector_open_session(expires_at,id);
create index if not exists ix_sector_open_request_created on ged.sector_open_request(created_at,id);
create index if not exists ix_sector_open_request_integration_created on ged.sector_open_request(integration_id,created_at);
