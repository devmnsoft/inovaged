-- InovaGED - Colunas e índice necessários para movimentação confiável de pastas GED.
-- Idempotente e sem criação de coluna path.

alter table if exists ged.folder
add column if not exists updated_at timestamptz null;

alter table if exists ged.folder
add column if not exists updated_by uuid null;

create index if not exists ix_folder_tenant_parent_name
on ged.folder(tenant_id, parent_id, lower(name))
where coalesce(reg_status, 'A') = 'A';
