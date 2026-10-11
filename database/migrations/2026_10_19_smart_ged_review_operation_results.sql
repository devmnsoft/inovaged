create table if not exists ged.smart_ged_review_operation_result (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null,
  operation_id uuid not null,
  suggestion_kind varchar(24) not null,
  suggestion_id uuid not null,
  document_id uuid not null,
  version_id uuid null,
  plan_version_id uuid null,
  classification_id uuid null,
  retention_rule_id uuid null,
  effect_status varchar(40) not null,
  result_status varchar(40) not null,
  effect_applied boolean not null default false,
  effective_at timestamptz null,
  audit_event text null,
  audit_recorded_at timestamptz null,
  result_payload_json jsonb not null default '{}'::jsonb,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  reg_status char(1) not null default 'A',
  constraint ck_smart_ged_review_operation_kind check (suggestion_kind in ('CLASSIFICATION','RETENTION')),
  constraint ck_smart_ged_review_operation_effect check (effect_status in ('EFFECT_CONFIRMED','NO_EFFECT','INDETERMINATE')),
  constraint ck_smart_ged_review_operation_result check (result_status in ('EFFECT_CONFIRMED','NO_EFFECT','FAILED_AFTER_EFFECT','FAILED_BEFORE_EFFECT','FINALIZED','INDETERMINATE'))
);

create unique index if not exists ux_smart_ged_review_operation_result_operation
  on ged.smart_ged_review_operation_result(tenant_id, operation_id)
  where reg_status = 'A';

create index if not exists ix_smart_ged_review_operation_result_suggestion
  on ged.smart_ged_review_operation_result(tenant_id, suggestion_kind, suggestion_id)
  where reg_status = 'A';

create index if not exists ix_smart_ged_review_operation_result_document
  on ged.smart_ged_review_operation_result(tenant_id, document_id, version_id)
  where reg_status = 'A';
