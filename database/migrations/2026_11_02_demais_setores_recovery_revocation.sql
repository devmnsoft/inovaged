-- Revocation survives reactivation without deleting request fingerprints or history.
alter table ged.sector_entry_ticket add column if not exists revoked_at timestamptz;
alter table ged.sector_open_request add column if not exists revoked_at timestamptz;
update ged.sector_entry_ticket t set revoked_at=now(),consumed_at=coalesce(consumed_at,now())
from ged.sector_integration i where i.id=t.integration_id and not i.active and t.revoked_at is null;
update ged.sector_open_request r set revoked_at=now()
from ged.sector_integration i where i.id=r.integration_id and not i.active and r.revoked_at is null;
