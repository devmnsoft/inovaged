-- Versioned pre-CMS fixture used to prove forward-compatible, data-preserving migrations.
CREATE SCHEMA IF NOT EXISTS ged;
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE TABLE IF NOT EXISTS ged.folder (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(), tenant_id uuid NOT NULL,
  parent_id uuid NULL, name text NOT NULL, created_at timestamptz NOT NULL DEFAULT now(),
  reg_status char(1) NOT NULL DEFAULT 'A'
);
INSERT INTO ged.folder (id, tenant_id, name)
VALUES ('00000000-0000-0000-0000-000000000101', '00000000-0000-0000-0000-000000000001', 'fixture-pre-cms')
ON CONFLICT (id) DO NOTHING;
