begin;

-- 1. Evolução da entidade de etiquetas avulsas para ciclo de vida completo
alter table if exists ged.label_manual_instance add column if not exists name varchar(250) null;
alter table if exists ged.label_manual_instance add column if not exists printed_at timestamptz null;
alter table if exists ged.label_manual_instance add column if not exists archived_at timestamptz null;
alter table if exists ged.label_manual_instance add column if not exists archived_by uuid null;

create index if not exists ix_label_manual_instance_status on ged.label_manual_instance(tenant_id, status, updated_at desc) where reg_status='ACTIVE';

-- 2. Idempotência e concorrência na emissão de impressões de etiqueta
alter table if exists ged.label_print add column if not exists client_action_id uuid null;
create unique index if not exists ux_label_print_client_action on ged.label_print(tenant_id, client_action_id) where client_action_id is not null and reg_status='A';

alter table if exists ged.label_print_history add column if not exists client_action_id uuid null;
create index if not exists ix_label_print_history_client_action on ged.label_print_history(tenant_id, client_action_id) where client_action_id is not null;

commit;
