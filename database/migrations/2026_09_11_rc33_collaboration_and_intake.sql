-- RC33: additive governance for collaborative label drafts and intake reviews.
alter table if exists ged.label_template_design
    add column if not exists lock_version bigint not null default 1;

create table if not exists ged.document_intake_review (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    document_id uuid not null,
    status varchar(32) not null default 'PENDING' check (status in ('PENDING','REVIEWED','NEEDS_CORRECTION')),
    reviewed_by uuid null,
    reviewed_at timestamptz null,
    notes text null,
    created_at timestamptz not null default now(),
    reg_status varchar(16) not null default 'A'
);
create unique index if not exists ux_document_intake_review_active
    on ged.document_intake_review(tenant_id, document_id) where reg_status='A';
create index if not exists ix_document_intake_review_queue
    on ged.document_intake_review(tenant_id, status, created_at desc) where reg_status='A';
