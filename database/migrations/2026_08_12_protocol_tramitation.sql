-- Tramitação definitiva de protocolos. A migration é aditiva e reaplicável.
CREATE SCHEMA IF NOT EXISTS ged;

CREATE TABLE IF NOT EXISTS ged.protocol_tramitation (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    protocol_request_id uuid NOT NULL,
    origin_sector_id uuid NULL,
    origin_sector_name text NULL,
    destination_sector_id uuid NOT NULL,
    destination_sector_name text NOT NULL,
    responsible_user_id uuid NULL,
    forwarded_by uuid NOT NULL,
    received_by uuid NULL,
    reason text NOT NULL,
    return_reason text NULL,
    status text NOT NULL DEFAULT 'PENDING_RECEIPT',
    forwarded_at timestamptz NOT NULL DEFAULT now(),
    received_at timestamptz NULL,
    returned_at timestamptz NULL,
    completed_at timestamptz NULL,
    updated_at timestamptz NULL,
    reg_status char(1) NOT NULL DEFAULT 'A'
);

ALTER TABLE ged.protocol_tramitation ADD COLUMN IF NOT EXISTS tenant_id uuid;
ALTER TABLE ged.protocol_tramitation ADD COLUMN IF NOT EXISTS protocol_request_id uuid;
ALTER TABLE ged.protocol_tramitation ADD COLUMN IF NOT EXISTS status text NOT NULL DEFAULT 'PENDING_RECEIPT';
ALTER TABLE ged.protocol_tramitation ADD COLUMN IF NOT EXISTS updated_at timestamptz NULL;
ALTER TABLE ged.protocol_tramitation ADD COLUMN IF NOT EXISTS reg_status char(1) NOT NULL DEFAULT 'A';

DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_protocol_tramitation_status' AND conrelid='ged.protocol_tramitation'::regclass) THEN
    ALTER TABLE ged.protocol_tramitation ADD CONSTRAINT ck_protocol_tramitation_status
      CHECK (status IN ('PENDING_RECEIPT','RECEIVED','RETURNED','COMPLETED','CANCELLED'));
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_protocol_tramitation_request' AND conrelid='ged.protocol_tramitation'::regclass) THEN
    ALTER TABLE ged.protocol_tramitation ADD CONSTRAINT fk_protocol_tramitation_request
      FOREIGN KEY (protocol_request_id) REFERENCES ged.protocol_request(id);
  END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_protocol_tramitation_inbox
  ON ged.protocol_tramitation(tenant_id, destination_sector_id, status, forwarded_at DESC)
  WHERE reg_status='A';
CREATE INDEX IF NOT EXISTS ix_protocol_tramitation_protocol
  ON ged.protocol_tramitation(tenant_id, protocol_request_id, forwarded_at DESC)
  WHERE reg_status='A';
