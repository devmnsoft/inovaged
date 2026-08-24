-- Este script é opcional para demonstração/homologação.
-- Não deve sobrescrever dados produtivos.
-- Idempotente: não apaga dados, não altera senhas e não atualiza registros existentes.

begin;
create schema if not exists ged;

-- Catálogo isolado de demonstração. O uso de uma tabela própria evita inferir o
-- formato de instalações legadas e permite ao importador homologado materializar
-- os registros somente após validar o schema daquele ambiente.
create table if not exists ged.release_candidate_demo_data (
    id uuid primary key,
    entity_type text not null,
    entity_code text not null,
    tenant_id uuid not null,
    safe_payload jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    constraint uq_release_candidate_demo_data unique (tenant_id, entity_type, entity_code)
);

insert into ged.release_candidate_demo_data(id, entity_type, entity_code, tenant_id, safe_payload) values
('26080000-0000-4000-8000-000000000001','TENANT','RC-DEMO','26080000-0000-4000-8000-000000000000','{"name":"InovaGED Homologação","environment":"DEMO"}'),
('26080000-0000-4000-8000-000000000002','ADMIN_INVITATION','RC-ADMIN','26080000-0000-4000-8000-000000000000','{"login":"admin.rc","requires_password_setup":true,"note":"Nenhuma senha é criada ou alterada por este seed"}'),
('26080000-0000-4000-8000-000000000003','LABEL_TEMPLATE','BOX-STANDARD','26080000-0000-4000-8000-000000000000','{"name":"Etiqueta padrão de caixa","version":1}'),
('26080000-0000-4000-8000-000000000004','LOCDESK_TEMPLATE','RC-LOCDESK','26080000-0000-4000-8000-000000000000','{"name":"Estação de homologação","enabled":false}'),
('26080000-0000-4000-8000-000000000005','SECURITY_CONFIGURATION','RC-SECURITY','26080000-0000-4000-8000-000000000000','{"permission_mode":"AUDIT_ONLY","requires_admin_activation":true}'),
('26080000-0000-4000-8000-000000000006','PHYSICAL_LOCATION','ARQ-A-01','26080000-0000-4000-8000-000000000000','{"name":"Arquivo A / Estante 01"}'),
('26080000-0000-4000-8000-000000000007','PHYSICAL_BOX','CX-RC-001','26080000-0000-4000-8000-000000000000','{"location":"ARQ-A-01","title":"Caixa de homologação"}'),
('26080000-0000-4000-8000-000000000008','DOCUMENT','DOC-RC-001','26080000-0000-4000-8000-000000000000','{"title":"Documento demonstrativo sem conteúdo pessoal","box":"CX-RC-001"}'),
('26080000-0000-4000-8000-000000000009','CLASSIFICATION','PCD-RC-001','26080000-0000-4000-8000-000000000000','{"title":"Classificação demonstrativa","status":"DRAFT"}'),
('26080000-0000-4000-8000-000000000010','RETENTION_BATCH','RET-RC-001','26080000-0000-4000-8000-000000000000','{"title":"Lote demonstrativo","status":"DRAFT"}')
on conflict (tenant_id, entity_type, entity_code) do nothing;

insert into ged.release_candidate_demo_data(id, entity_type, entity_code, tenant_id, safe_payload)
select gen_random_uuid(), 'PERMISSION', code, '26080000-0000-4000-8000-000000000000', jsonb_build_object('code', code, 'grant_to_default_admin', true)
from unnest(array[
 'admin.full','database.readiness.view','database.readiness.apply','system.incidents.view','system.incidents.manage',
 'release.readiness.view','labels.view','labels.print','labels.templates.manage','physical.archive.view',
 'physical.archive.manage','retention.view','retention.manage','instruments.view','instruments.publish',
 'loans.view','loans.manage','ocr.view','smartsearch.view'
]) code
on conflict (tenant_id, entity_type, entity_code) do nothing;

commit;
