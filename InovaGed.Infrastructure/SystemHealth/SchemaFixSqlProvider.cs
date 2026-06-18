using InovaGed.Application.SystemHealth;

namespace InovaGed.Infrastructure.SystemHealth;

public sealed class SchemaFixSqlProvider : ISchemaFixSqlProvider
{
    private const string ConsolidatedScriptName = "dynamic-schema-repair";

    private const string DocumentSearchTenantVersionIndexSql = """
do $$
begin
    if exists (
        select 1
        from information_schema.columns
        where table_schema = 'ged'
          and table_name = 'document_search'
          and column_name = 'tenant_id'
    ) and exists (
        select 1
        from information_schema.columns
        where table_schema = 'ged'
          and table_name = 'document_search'
          and column_name = 'document_version_id'
    ) then
        execute 'create index if not exists ix_ged_document_search_tenant_document_version on ged.document_search(tenant_id, document_version_id)';

    elsif exists (
        select 1
        from information_schema.columns
        where table_schema = 'ged'
          and table_name = 'document_search'
          and column_name = 'tenant_id'
    ) and exists (
        select 1
        from information_schema.columns
        where table_schema = 'ged'
          and table_name = 'document_search'
          and column_name = 'version_id'
    ) then
        execute 'create index if not exists ix_ged_document_search_tenant_version on ged.document_search(tenant_id, version_id)';

    elsif exists (
        select 1
        from information_schema.columns
        where table_schema = 'ged'
          and table_name = 'document_search'
          and column_name = 'tenant_id'
    ) and exists (
        select 1
        from information_schema.columns
        where table_schema = 'ged'
          and table_name = 'document_search'
          and column_name = 'document_id'
    ) then
        execute 'create index if not exists ix_ged_document_search_tenant_document on ged.document_search(tenant_id, document_id)';

    else
        raise notice 'Índice ix_ged_document_search_tenant_version não criado: nenhuma coluna de versão/documento compatível encontrada em ged.document_search.';
    end if;
end $$;
""";
    private const string UploadBatchItemStatusConstraintSql = """
create schema if not exists ged;

alter table if exists ged.upload_batch_item
add column if not exists status text not null default 'PENDING';

alter table if exists ged.upload_batch_item
add column if not exists processing_warning text null;

update ged.upload_batch_item
set status = 'PENDING'
where status is null or trim(status) = '';

update ged.upload_batch_item
set status = 'CANCELLED'
where upper(status) = 'CANCELED';

update ged.upload_batch_item
set status = 'QUEUED'
where upper(status) in ('OCR_QUEUED', 'PREVIEW_QUEUED', 'SMART_INDEX_QUEUED');

update ged.upload_batch_item
set status = upper(status)
where status <> upper(status);

update ged.upload_batch_item
set status = 'PENDING', processing_warning = concat_ws(' | ', processing_warning, 'Status legado incompatível normalizado para PENDING: ' || status)
where status not in ('PENDING','RECEIVING','SAVED','DOCUMENT_CREATED','QUEUED','COMPLETED','ERROR','SKIPPED','ABORTED','RETRYABLE','DUPLICATE','CANCELLED');

do $$
begin
    if exists (select 1 from pg_constraint where conname = 'ck_upload_batch_item_status' and conrelid = 'ged.upload_batch_item'::regclass) then
        alter table ged.upload_batch_item drop constraint ck_upload_batch_item_status;
    end if;
end $$;

alter table ged.upload_batch_item
add constraint ck_upload_batch_item_status
check (status in ('PENDING','RECEIVING','SAVED','DOCUMENT_CREATED','QUEUED','COMPLETED','ERROR','SKIPPED','ABORTED','RETRYABLE','DUPLICATE','CANCELLED'));

create index if not exists ix_upload_batch_item_tenant_batch_status
on ged.upload_batch_item(tenant_id, batch_id, status);

create index if not exists ix_upload_batch_item_retryable
on ged.upload_batch_item(tenant_id, batch_id, status, can_retry)
where status in ('ERROR', 'ABORTED', 'RETRYABLE');
""";

    private static readonly IReadOnlyDictionary<string, SchemaFixDto> Fixes = BuildFixes();

    public Task<IReadOnlyList<SchemaFixDto>> GetFixesAsync(SchemaHealthReportDto report, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var fixes = report.Checks
            .Where(c => !c.Success)
            .Select(c =>
            {
                var fix = ResolveFix(c.Id, c.ObjectName);
                if (fix is null)
                    return null;

                var clone = Clone(fix);
                if (IsDocumentSearchTenantVersionCheck(c.Id) && !c.CanAutoFix && c.Message.Contains("Correção automática indisponível", StringComparison.OrdinalIgnoreCase))
                {
                    clone.CanAutoFix = false;
                    clone.Description = "Não foi possível gerar correção automática porque nenhuma coluna compatível foi encontrada.";
                }

                return clone;
            })
            .Where(f => f is not null)
            .GroupBy(f => f!.CheckId, StringComparer.OrdinalIgnoreCase)
            .Select(g => Clone(g.First()!))
            .ToList();

        return Task.FromResult<IReadOnlyList<SchemaFixDto>>(fixes);
    }

    public bool TryGetKnownFix(string checkId, out SchemaFixDto fix)
    {
        if (Fixes.TryGetValue(NormalizeId(checkId), out var value))
        {
            fix = Clone(value);
            return true;
        }

        fix = new SchemaFixDto();
        return false;
    }

    private static SchemaFixDto? ResolveFix(string checkId, string objectName)
    {
        if (Fixes.TryGetValue(NormalizeId(checkId), out var byId))
            return byId;

        return Fixes.Values.FirstOrDefault(f => string.Equals(f.ObjectName, objectName, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyDictionary<string, SchemaFixDto> BuildFixes()
    {
        var fixes = new List<SchemaFixDto>
        {
            Table("GED_TABLE_CODE_SEQUENCE", "ged.code_sequence", "Code Generation", "Cria a tabela de sequência de códigos automáticos.", """
create schema if not exists ged;
create extension if not exists pgcrypto;

create table if not exists ged.code_sequence (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    entity_name text not null,
    prefix text not null default 'COD',
    current_value bigint not null default 0,
    padding int not null default 4,
    created_at timestamptz not null default now(),
    updated_at timestamptz null,
    reg_status char(1) not null default 'A'
);

alter table ged.code_sequence add column if not exists tenant_id uuid;
alter table ged.code_sequence add column if not exists entity_name text;
alter table ged.code_sequence add column if not exists prefix text;
alter table ged.code_sequence add column if not exists current_value bigint not null default 0;
alter table ged.code_sequence add column if not exists padding int not null default 4;
alter table ged.code_sequence add column if not exists created_at timestamptz not null default now();
alter table ged.code_sequence add column if not exists updated_at timestamptz null;
alter table ged.code_sequence add column if not exists reg_status char(1) not null default 'A';

create unique index if not exists ux_code_sequence_tenant_entity
on ged.code_sequence(tenant_id, entity_name)
where coalesce(reg_status,'A')='A';

create index if not exists ix_code_sequence_tenant
on ged.code_sequence(tenant_id);
"""),
            Table("GED_TABLE_APP_AUDIT_LOG", "ged.app_audit_log", "Audit", "Cria a tabela de auditoria técnica da aplicação.", """
create extension if not exists pgcrypto;

create table if not exists ged.app_audit_log (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid null,
    user_id uuid null,
    user_name text null,
    action text not null default 'INFO',
    event_type text not null default 'INFO',
    source text null,
    entity_name text null,
    entity_id text null,
    method text null,
    path text null,
    status_code int null,
    message text null,
    details jsonb null,
    correlation_id text null,
    ip_address text null,
    user_agent text null,
    created_at timestamptz not null default now(),
    reg_status char(1) not null default 'A'
);
"""),
            Table("GED_TABLE_SCHEMA_MIGRATION_HISTORY", "ged.schema_migration_history", "Migrations", "Cria o histórico padronizado de migrations.", """
create extension if not exists pgcrypto;

create table if not exists ged.schema_migration_history (
    id uuid primary key default gen_random_uuid(),
    script_name text not null,
    applied_at timestamptz not null default now(),
    applied_by text null,
    checksum_sha256 text null,
    success boolean not null default true,
    notes text null
);

create unique index if not exists ux_schema_migration_history_script
on ged.schema_migration_history(script_name);
"""),
            Table("GED_TABLE_UPLOAD_SESSION", "ged.upload_session", "Upload chunks", "Cria a tabela de sessões de upload em partes.", """
create extension if not exists pgcrypto;

create table if not exists ged.upload_session (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    user_id uuid not null,
    folder_id uuid not null,
    requested_folder_id uuid null,
    batch_id uuid null,
    original_file_name text not null,
    content_type text null,
    total_size_bytes bigint not null default 0,
    chunk_size_bytes int not null default 0,
    total_chunks int not null default 0,
    received_chunks int not null default 0,
    status text not null default 'STARTED',
    temp_path text null,
    document_id uuid null,
    version_id uuid null,
    error_message text null,
    correlation_id text null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    completed_at timestamptz null,
    reg_status char(1) not null default 'A'
);
"""),
            Table("GED_TABLE_UPLOAD_SESSION_CHUNK", "ged.upload_session_chunk", "Upload chunks", "Cria a tabela de chunks recebidos.", """
create extension if not exists pgcrypto;

create table if not exists ged.upload_session_chunk (
    id uuid primary key default gen_random_uuid(),
    session_id uuid not null,
    chunk_index int not null,
    size_bytes bigint not null default 0,
    checksum_sha256 text null,
    temp_path text null,
    received_at timestamptz not null default now(),
    reg_status char(1) not null default 'A',
    unique(session_id, chunk_index)
);
"""),
            Table("GED_TABLE_DOCUMENT_PARTIAL_PART", "ged.document_partial_part", "GED partial documents", "Cria a tabela de partes de documentos fracionados.", """
create extension if not exists pgcrypto;

create table if not exists ged.document_partial_part (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    document_id uuid not null,
    version_id uuid not null,
    partial_group_id uuid not null,
    part_number int not null,
    total_parts int null,
    file_name text null,
    size_bytes bigint null,
    uploaded_at_utc timestamptz not null default now(),
    uploaded_by uuid null,
    status text not null default 'UPLOADED',
    notes text null,
    created_at timestamptz not null default now(),
    reg_status char(1) not null default 'A'
);
"""),
            Table("GED_TABLE_OCR_AUTO_SCHEDULE_RUN", "ged.ocr_auto_schedule_run", "OCR Auto Schedule", "Cria a tabela de execuções do agendamento automático de OCR.", """
create extension if not exists pgcrypto;
create schema if not exists ged;
create table if not exists ged.ocr_auto_schedule_run (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    started_at_utc timestamptz not null default now(),
    finished_at_utc timestamptz null,
    status text not null default 'STARTED',
    candidates_found int not null default 0,
    enqueued_count int not null default 0,
    skipped_count int not null default 0,
    failed_count int not null default 0,
    message text null,
    correlation_id text null,
    created_at timestamptz not null default now()
);
"""),
            Table("GED_TABLE_OCR_AUTO_SCHEDULE_RUN_ITEM", "ged.ocr_auto_schedule_run_item", "OCR Auto Schedule", "Cria a tabela de itens do agendamento automático de OCR.", """
create extension if not exists pgcrypto;
create schema if not exists ged;
create table if not exists ged.ocr_auto_schedule_run_item (
    id uuid primary key default gen_random_uuid(),
    run_id uuid not null,
    tenant_id uuid not null,
    document_id uuid null,
    version_id uuid null,
    file_name text null,
    status text not null default 'PENDING',
    reason text null,
    ocr_job_id uuid null,
    created_at timestamptz not null default now()
);
"""),
            Table("GED_TABLE_LOAN_REQUEST_ITEM", "ged.loan_request_item", "Loans", "Cria a tabela de itens de empréstimo com suporte a itens manuais.", """
create extension if not exists pgcrypto;
create schema if not exists ged;
create table if not exists ged.loan_request_item (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    loan_request_id uuid null,
    document_id uuid null,
    is_physical boolean not null default false,
    is_manual boolean not null default false,
    reference_code text null,
    description text null,
    document_type text null,
    patient_name text null,
    medical_record_number text null,
    box_code text null,
    physical_location text null,
    notes text null,
    document_version_id uuid null,
    created_at timestamptz not null default now(),
    reg_status char(1) not null default 'A'
);
"""),
            Table("GED_TABLE_LOAN_REQUEST_HISTORY", "ged.loan_request_history", "Loans History", "Cria a tabela de histórico rico de empréstimos.", """
create extension if not exists pgcrypto;
create schema if not exists ged;
create table if not exists ged.loan_request_history (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    loan_request_id uuid not null,
    old_status text null,
    new_status text null,
    action text not null default 'INFO',
    user_id uuid null,
    user_name text null,
    sector_id uuid null,
    sector_name text null,
    reason text null,
    internal_notes text null,
    metadata_json jsonb not null default '{}'::jsonb,
    correlation_id text null,
    created_at timestamptz not null default now(),
    reg_status char(1) not null default 'A'
);
"""),

            Table("GED_TABLE_PROTOCOL_REQUEST", "ged.protocol_request", "Protocolo", "Cria as tabelas consolidadas do módulo Protocolo.", """
create extension if not exists pgcrypto;
create schema if not exists ged;
create table if not exists ged.protocol_request (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    protocol_no text not null,
    requester_user_id uuid null,
    requester_name text null,
    requester_sector_id uuid null,
    requester_sector_name text null,
    assigned_sector_id uuid null,
    assigned_sector_name text null,
    assigned_user_id uuid null,
    assigned_user_name text null,
    title text not null default 'Solicitação de Protocolo',
    description text null,
    priority text not null default 'NORMAL',
    status text not null default 'REQUESTED',
    due_at timestamptz null,
    requested_at timestamptz not null default now(),
    updated_at timestamptz null,
    finished_at timestamptz null,
    reg_status char(1) not null default 'A',
    correlation_id text null,
    created_at timestamptz not null default now()
);
create table if not exists ged.protocol_request_item (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    protocol_request_id uuid not null,
    document_id uuid null,
    document_version_id uuid null,
    is_manual boolean not null default false,
    reference_code text null,
    description text null,
    document_type text null,
    patient_name text null,
    medical_record_number text null,
    box_code text null,
    physical_location text null,
    notes text null,
    created_at timestamptz not null default now(),
    reg_status char(1) not null default 'A'
);
create table if not exists ged.protocol_request_attachment (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    protocol_request_id uuid not null,
    file_name text not null,
    content_type text null,
    size_bytes bigint null,
    storage_path text not null,
    uploaded_by uuid null,
    uploaded_by_name text null,
    uploaded_at timestamptz not null default now(),
    reg_status char(1) not null default 'A'
);
create table if not exists ged.protocol_request_history (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    protocol_request_id uuid not null,
    old_status text null,
    new_status text null,
    action text not null default 'INFO',
    user_id uuid null,
    user_name text null,
    sector_id uuid null,
    sector_name text null,
    reason text null,
    internal_notes text null,
    metadata_json jsonb not null default '{}'::jsonb,
    correlation_id text null,
    created_at timestamptz not null default now(),
    reg_status char(1) not null default 'A'
);
"""),
            Table("GED_TABLE_PROTOCOL_REQUEST_ITEM", "ged.protocol_request_item", "Protocolo", "Cria as tabelas consolidadas do módulo Protocolo.", """
create extension if not exists pgcrypto;
create schema if not exists ged;
create table if not exists ged.protocol_request (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    protocol_no text not null,
    requester_user_id uuid null,
    requester_name text null,
    requester_sector_id uuid null,
    requester_sector_name text null,
    assigned_sector_id uuid null,
    assigned_sector_name text null,
    assigned_user_id uuid null,
    assigned_user_name text null,
    title text not null default 'Solicitação de Protocolo',
    description text null,
    priority text not null default 'NORMAL',
    status text not null default 'REQUESTED',
    due_at timestamptz null,
    requested_at timestamptz not null default now(),
    updated_at timestamptz null,
    finished_at timestamptz null,
    reg_status char(1) not null default 'A',
    correlation_id text null,
    created_at timestamptz not null default now()
);
create table if not exists ged.protocol_request_item (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    protocol_request_id uuid not null,
    document_id uuid null,
    document_version_id uuid null,
    is_manual boolean not null default false,
    reference_code text null,
    description text null,
    document_type text null,
    patient_name text null,
    medical_record_number text null,
    box_code text null,
    physical_location text null,
    notes text null,
    created_at timestamptz not null default now(),
    reg_status char(1) not null default 'A'
);
create table if not exists ged.protocol_request_attachment (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    protocol_request_id uuid not null,
    file_name text not null,
    content_type text null,
    size_bytes bigint null,
    storage_path text not null,
    uploaded_by uuid null,
    uploaded_by_name text null,
    uploaded_at timestamptz not null default now(),
    reg_status char(1) not null default 'A'
);
create table if not exists ged.protocol_request_history (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    protocol_request_id uuid not null,
    old_status text null,
    new_status text null,
    action text not null default 'INFO',
    user_id uuid null,
    user_name text null,
    sector_id uuid null,
    sector_name text null,
    reason text null,
    internal_notes text null,
    metadata_json jsonb not null default '{}'::jsonb,
    correlation_id text null,
    created_at timestamptz not null default now(),
    reg_status char(1) not null default 'A'
);
"""),
            Table("GED_TABLE_PROTOCOL_REQUEST_ATTACHMENT", "ged.protocol_request_attachment", "Protocolo", "Cria as tabelas consolidadas do módulo Protocolo.", """
create extension if not exists pgcrypto;
create schema if not exists ged;
create table if not exists ged.protocol_request (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    protocol_no text not null,
    requester_user_id uuid null,
    requester_name text null,
    requester_sector_id uuid null,
    requester_sector_name text null,
    assigned_sector_id uuid null,
    assigned_sector_name text null,
    assigned_user_id uuid null,
    assigned_user_name text null,
    title text not null default 'Solicitação de Protocolo',
    description text null,
    priority text not null default 'NORMAL',
    status text not null default 'REQUESTED',
    due_at timestamptz null,
    requested_at timestamptz not null default now(),
    updated_at timestamptz null,
    finished_at timestamptz null,
    reg_status char(1) not null default 'A',
    correlation_id text null,
    created_at timestamptz not null default now()
);
create table if not exists ged.protocol_request_item (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    protocol_request_id uuid not null,
    document_id uuid null,
    document_version_id uuid null,
    is_manual boolean not null default false,
    reference_code text null,
    description text null,
    document_type text null,
    patient_name text null,
    medical_record_number text null,
    box_code text null,
    physical_location text null,
    notes text null,
    created_at timestamptz not null default now(),
    reg_status char(1) not null default 'A'
);
create table if not exists ged.protocol_request_attachment (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    protocol_request_id uuid not null,
    file_name text not null,
    content_type text null,
    size_bytes bigint null,
    storage_path text not null,
    uploaded_by uuid null,
    uploaded_by_name text null,
    uploaded_at timestamptz not null default now(),
    reg_status char(1) not null default 'A'
);
create table if not exists ged.protocol_request_history (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    protocol_request_id uuid not null,
    old_status text null,
    new_status text null,
    action text not null default 'INFO',
    user_id uuid null,
    user_name text null,
    sector_id uuid null,
    sector_name text null,
    reason text null,
    internal_notes text null,
    metadata_json jsonb not null default '{}'::jsonb,
    correlation_id text null,
    created_at timestamptz not null default now(),
    reg_status char(1) not null default 'A'
);
"""),
            Table("GED_TABLE_PROTOCOL_REQUEST_HISTORY", "ged.protocol_request_history", "Protocolo", "Cria as tabelas consolidadas do módulo Protocolo.", """
create extension if not exists pgcrypto;
create schema if not exists ged;
create table if not exists ged.protocol_request (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    protocol_no text not null,
    requester_user_id uuid null,
    requester_name text null,
    requester_sector_id uuid null,
    requester_sector_name text null,
    assigned_sector_id uuid null,
    assigned_sector_name text null,
    assigned_user_id uuid null,
    assigned_user_name text null,
    title text not null default 'Solicitação de Protocolo',
    description text null,
    priority text not null default 'NORMAL',
    status text not null default 'REQUESTED',
    due_at timestamptz null,
    requested_at timestamptz not null default now(),
    updated_at timestamptz null,
    finished_at timestamptz null,
    reg_status char(1) not null default 'A',
    correlation_id text null,
    created_at timestamptz not null default now()
);
create table if not exists ged.protocol_request_item (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    protocol_request_id uuid not null,
    document_id uuid null,
    document_version_id uuid null,
    is_manual boolean not null default false,
    reference_code text null,
    description text null,
    document_type text null,
    patient_name text null,
    medical_record_number text null,
    box_code text null,
    physical_location text null,
    notes text null,
    created_at timestamptz not null default now(),
    reg_status char(1) not null default 'A'
);
create table if not exists ged.protocol_request_attachment (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    protocol_request_id uuid not null,
    file_name text not null,
    content_type text null,
    size_bytes bigint null,
    storage_path text not null,
    uploaded_by uuid null,
    uploaded_by_name text null,
    uploaded_at timestamptz not null default now(),
    reg_status char(1) not null default 'A'
);
create table if not exists ged.protocol_request_history (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    protocol_request_id uuid not null,
    old_status text null,
    new_status text null,
    action text not null default 'INFO',
    user_id uuid null,
    user_name text null,
    sector_id uuid null,
    sector_name text null,
    reason text null,
    internal_notes text null,
    metadata_json jsonb not null default '{}'::jsonb,
    correlation_id text null,
    created_at timestamptz not null default now(),
    reg_status char(1) not null default 'A'
);
"""),
            Table("GED_TABLE_UPLOAD_DUPLICATE_DECISION", "ged.upload_duplicate_decision", "Upload batch", "Cria a tabela de auditoria das decisões de possível duplicidade no upload.", """
create extension if not exists pgcrypto;
create schema if not exists ged;
create table if not exists ged.upload_duplicate_decision (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    batch_id uuid null,
    upload_batch_item_id uuid null,
    document_id uuid null,
    duplicate_of_document_id uuid null,
    file_name text not null,
    duplicate_scope text not null,
    selected_action text not null,
    confirmed_duplicate_upload boolean not null default false,
    reason text null,
    decided_by uuid null,
    decided_at timestamptz not null default now(),
    details_json jsonb null,
    reg_status char(1) not null default 'A'
);
"""),
            Table("GED_TABLE_DOCUMENT_QUALITY_RUN", "ged.document_quality_run", "Qualidade Documental", "Cria a tabela de execuções da Qualidade Documental.", """
create extension if not exists pgcrypto;
create schema if not exists ged;
create table if not exists ged.document_quality_run (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    started_at_utc timestamptz not null default now(),
    finished_at_utc timestamptz null,
    status text not null default 'STARTED',
    total_documents int not null default 0,
    excellent_count int not null default 0,
    good_count int not null default 0,
    warning_count int not null default 0,
    critical_count int not null default 0,
    failed_count int not null default 0,
    message text null,
    correlation_id text null,
    created_at timestamptz not null default now()
);
"""),
            Table("GED_TABLE_DOCUMENT_QUALITY_RESULT", "ged.document_quality_result", "Qualidade Documental", "Cria a tabela de resultados da Qualidade Documental.", """
create extension if not exists pgcrypto;
create schema if not exists ged;
create table if not exists ged.document_quality_result (
    id uuid primary key default gen_random_uuid(),
    run_id uuid null,
    tenant_id uuid not null,
    document_id uuid not null,
    current_version_id uuid null,
    quality_score int not null default 0,
    quality_status text not null default 'Não analisado',
    has_ocr boolean not null default false,
    has_ocr_error boolean not null default false,
    has_classification boolean not null default false,
    has_document_type boolean not null default false,
    has_required_metadata boolean not null default false,
    is_partial_document boolean not null default false,
    is_partial_incomplete boolean not null default false,
    is_ready_to_consolidate boolean not null default false,
    is_consolidated boolean not null default false,
    storage_file_exists boolean null,
    has_possible_duplicate boolean not null default false,
    has_lgpd_risk boolean not null default false,
    issues_json jsonb not null default '[]'::jsonb,
    recommendations_json jsonb not null default '[]'::jsonb,
    next_action text null,
    analyzed_at_utc timestamptz not null default now(),
    created_at timestamptz not null default now()
);
""")
        };

        fixes.Add(new SchemaFixDto
        {
            CheckId = "GED_CONSTRAINT_UPLOAD_BATCH_ITEM_STATUS",
            ObjectName = "ged.upload_batch_item.ck_upload_batch_item_status",
            Area = "Upload batch",
            Description = "Normaliza status legados e recria a constraint de status dos itens de upload em lote.",
            FixSql = UploadBatchItemStatusConstraintSql.Trim() + Environment.NewLine,
            CanAutoFix = true,
            RiskLevel = "Medium",
            FixType = "Constraint",
            Dependencies = [new SchemaObjectDependency { Type = "Table", Schema = "ged", Table = "upload_batch_item" }],
            ScriptName = ConsolidatedScriptName
        });
        AddColumnFixes(fixes);
        AddIndexFixes(fixes);
        return fixes.ToDictionary(f => NormalizeId(f.CheckId), StringComparer.OrdinalIgnoreCase);
    }

    private static void AddColumnFixes(List<SchemaFixDto> fixes)
    {
        AddColumn(fixes, "ged.document", "reg_status", "char(1) not null default 'A'", "GED soft delete");
        AddColumn(fixes, "ged.document", "deleted_at", "timestamptz null", "GED soft delete");
        AddColumn(fixes, "ged.document", "deleted_by", "uuid null", "GED soft delete");
        AddColumn(fixes, "ged.document", "deleted_reason", "text null", "GED soft delete");
        AddColumn(fixes, "ged.document", "updated_at", "timestamptz null", "GED soft delete");
        AddColumn(fixes, "ged.document", "updated_by", "uuid null", "GED soft delete");
        AddColumn(fixes, "ged.document", "is_document_incomplete", "boolean not null default false", "GED incomplete documents");
        AddColumn(fixes, "ged.document", "incomplete_reason", "text null", "GED incomplete documents");
        AddColumn(fixes, "ged.document", "incomplete_source", "text null", "GED incomplete documents");
        AddColumn(fixes, "ged.document_version", "partial_group_id", "uuid null", "GED partial documents");
        AddColumn(fixes, "ged.document_version", "partial_part_number", "int null", "GED partial documents");
        AddColumn(fixes, "ged.document_version", "partial_total_parts", "int null", "GED partial documents");
        AddColumn(fixes, "ged.document_version", "partial_status", "text not null default 'NOT_PARTIAL'", "GED partial documents");
        AddColumn(fixes, "ged.document_version", "is_partial_document", "boolean not null default false", "GED partial documents");
        AddColumn(fixes, "ged.document_version", "consolidated_version_id", "uuid null", "GED partial documents");
        AddColumn(fixes, "ged.document_version", "uploaded_at_utc", "timestamptz null", "GED versions");
        AddColumn(fixes, "ged.document_version", "is_document_incomplete", "boolean not null default false", "GED incomplete documents");
        AddColumn(fixes, "ged.document_version", "incomplete_reason", "text null", "GED incomplete documents");
        AddColumn(fixes, "ged.document_version", "incomplete_source", "text null", "GED incomplete documents");
        AddColumn(fixes, "ged.upload_batch", "created_by", "uuid null", "GED Upload History");
        AddColumn(fixes, "ged.upload_batch", "created_by_name", "text null", "GED Upload History");
        AddColumn(fixes, "ged.upload_batch", "folder_id", "uuid null", "GED Upload History");
        AddColumn(fixes, "ged.upload_batch", "requested_folder_id", "uuid null", "GED Upload History");
        AddColumn(fixes, "ged.upload_batch", "status", "text not null default 'OPEN'", "GED Upload History");
        AddColumn(fixes, "ged.upload_batch", "total_files", "int not null default 0", "GED Upload History");
        AddColumn(fixes, "ged.upload_batch", "success_files", "int not null default 0", "GED Upload History");
        AddColumn(fixes, "ged.upload_batch", "failed_files", "int not null default 0", "GED Upload History");
        AddColumn(fixes, "ged.upload_batch", "skipped_files", "int not null default 0", "GED Upload History");
        AddColumn(fixes, "ged.upload_batch", "source_ip", "text null", "GED Upload History");
        AddColumn(fixes, "ged.upload_batch", "user_agent", "text null", "GED Upload History");
        AddColumn(fixes, "ged.upload_batch", "correlation_id", "text null", "GED Upload History");
        AddColumn(fixes, "ged.upload_batch", "started_at", "timestamptz null", "GED Upload History");
        AddColumn(fixes, "ged.upload_batch", "finished_at", "timestamptz null", "GED Upload History");
        AddColumn(fixes, "ged.upload_batch", "created_at", "timestamptz not null default now()", "GED Upload History");
        AddColumn(fixes, "ged.upload_batch", "updated_at", "timestamptz null", "GED Upload History");
        AddColumn(fixes, "ged.upload_batch", "reg_status", "char(1) not null default 'A'", "GED Upload History");
        AddColumn(fixes, "ged.upload_batch", "acknowledged_at", "timestamptz null", "GED Upload History");
        AddColumn(fixes, "ged.upload_batch", "acknowledged_by", "uuid null", "GED Upload History");
        AddColumn(fixes, "ged.upload_batch", "problem_seen", "boolean not null default false", "GED Upload History");
        AddColumn(fixes, "ged.upload_batch", "user_notes", "text null", "GED Upload History");
        AddColumn(fixes, "ged.upload_batch", "options_json", "jsonb not null default {}::jsonb", "Upload batch");
        AddColumn(fixes, "ged.upload_batch_item", "upload_client_id", "text null", "Upload batch");
        AddColumn(fixes, "ged.upload_batch_item", "content_hash", "text null", "Upload batch");
        AddColumn(fixes, "ged.upload_batch_item", "duplicate_of_document_id", "uuid null", "Upload batch");
        AddColumn(fixes, "ged.upload_batch_item", "duplicate_scope", "text null", "Upload batch");
        AddColumn(fixes, "ged.upload_batch_item", "duplicate_resolution", "text null", "Upload batch");
        AddColumn(fixes, "ged.upload_batch_item", "confirmed_duplicate_upload", "boolean not null default false", "Upload batch");
        AddColumn(fixes, "ged.upload_batch_item", "mark_as_incomplete", "boolean not null default false", "Upload batch");
        AddColumn(fixes, "ged.upload_batch_item", "incomplete_reason", "text null", "Upload batch");
        AddColumn(fixes, "ged.upload_batch_item", "retry_after_at", "timestamptz null", "Upload batch");
        AddColumn(fixes, "ged.upload_batch_item", "processing_warning", "text null", "Upload batch");
        AddColumn(fixes, "ged.upload_batch_item", "error_step", "text null", "GED Upload History");
        AddColumn(fixes, "ged.upload_batch_item", "can_retry", "boolean not null default false", "GED Upload History");
        AddColumn(fixes, "ged.upload_batch_item", "elapsed_ms", "bigint null", "GED Upload History");
        AddColumn(fixes, "ged.upload_batch_item", "finished_at", "timestamptz null", "GED Upload History");
        AddColumn(fixes, "ged.upload_batch_item", "updated_at", "timestamptz null", "GED Upload History");
        AddColumn(fixes, "ged.upload_batch_item", "upload_session_id", "uuid null", "Upload batch");
        AddColumn(fixes, "ged.upload_batch_item", "uploaded_by_name", "text null", "GED Upload History");
        AddColumn(fixes, "ged.upload_session", "tenant_id", "uuid null", "Upload chunks");
        AddColumn(fixes, "ged.upload_session", "total_chunks", "int not null default 0", "Upload chunks");
        AddColumn(fixes, "ged.upload_session_chunk", "session_id", "uuid null", "Upload chunks");
        AddColumn(fixes, "ged.upload_session_chunk", "chunk_index", "int null", "Upload chunks");
        AddColumn(fixes, "ged.ocr_job", "id", "bigint null", "OCR");
        AddColumn(fixes, "ged.ocr_job", "tenant_id", "uuid null", "OCR");
        AddColumn(fixes, "ged.ocr_job", "document_version_id", "uuid null", "OCR");
        AddColumn(fixes, "ged.ocr_job", "status", "ged.ocr_status_enum null default 'PENDING'", "OCR");
        AddColumn(fixes, "ged.ocr_job", "requested_at", "timestamptz null default now()", "OCR");
        AddColumn(fixes, "ged.ocr_job", "started_at", "timestamptz null", "OCR");
        AddColumn(fixes, "ged.ocr_job", "finished_at", "timestamptz null", "OCR");
        AddColumn(fixes, "ged.ocr_job", "error_message", "text null", "OCR");
        AddColumn(fixes, "ged.ocr_job", "error_details_json", "jsonb null", "OCR");
        AddColumn(fixes, "ged.ocr_job", "attempt_count", "int not null default 0", "OCR");
        AddColumn(fixes, "ged.ocr_job", "worker_id", "text null", "OCR");
        AddColumn(fixes, "ged.ocr_job", "locked_at", "timestamptz null", "OCR");
        AddColumn(fixes, "ged.ocr_job", "locked_by", "text null", "OCR");
        AddColumn(fixes, "ged.ocr_job", "updated_at", "timestamptz null", "OCR");
        AddColumn(fixes, "ged.ocr_job", "next_attempt_at", "timestamptz null", "OCR");
        AddColumn(fixes, "ged.ocr_job", "failure_code", "text null", "OCR");
        AddColumn(fixes, "ged.ocr_job", "reg_status", "char(1) not null default 'A'", "OCR");
        AddColumn(fixes, "ged.app_audit_log", "created_at", "timestamptz not null default now()", "SystemLogs");
        AddColumn(fixes, "ged.app_audit_log", "user_name", "text null", "SystemLogs");
        AddColumn(fixes, "ged.audit_log", "created_at", "timestamptz null", "SystemLogs");
        AddColumn(fixes, "ged.audit_log", "user_name", "text null", "SystemLogs");
        AddColumn(fixes, "ged.ocr_auto_schedule_run", "tenant_id", "uuid null", "OCR Auto Schedule");
        AddColumn(fixes, "ged.ocr_auto_schedule_run", "started_at_utc", "timestamptz not null default now()", "OCR Auto Schedule");
        AddColumn(fixes, "ged.ocr_auto_schedule_run", "status", "text not null default 'STARTED'", "OCR Auto Schedule");
        AddColumn(fixes, "ged.ocr_auto_schedule_run_item", "run_id", "uuid null", "OCR Auto Schedule");
        AddColumn(fixes, "ged.ocr_auto_schedule_run_item", "status", "text not null default 'PENDING'", "OCR Auto Schedule");
        AddColumn(fixes, "ged.loan_request_item", "is_manual", "boolean not null default false", "Loans");
        AddColumn(fixes, "ged.loan_request_item", "is_physical", "boolean not null default false", "Loans");
        AddColumn(fixes, "ged.loan_request_item", "reference_code", "text null", "Loans");
        AddColumn(fixes, "ged.loan_request_item", "description", "text null", "Loans");
        AddColumn(fixes, "ged.loan_request_item", "document_type", "text null", "Loans");
        AddColumn(fixes, "ged.loan_request_item", "patient_name", "text null", "Loans");
        AddColumn(fixes, "ged.loan_request_item", "medical_record_number", "text null", "Loans");
        AddColumn(fixes, "ged.loan_request_item", "box_code", "text null", "Loans");
        AddColumn(fixes, "ged.loan_request_item", "physical_location", "text null", "Loans");
        AddColumn(fixes, "ged.loan_request_item", "notes", "text null", "Loans");
        AddColumn(fixes, "ged.loan_request_item", "document_version_id", "uuid null", "Loans");
        AddColumn(fixes, "ged.loan_request_item", "created_at", "timestamptz not null default now()", "Loans");
        AddColumn(fixes, "ged.loan_request_item", "loan_request_id", "uuid null", "Loans");
        AddColumn(fixes, "ged.loan_request_item", "reg_status", "char(1) not null default 'A'", "Loans");
        AddColumn(fixes, "ged.loan_request", "requester_sector", "text null", "Loans");
        AddColumn(fixes, "ged.loan_request", "sector_id", "uuid null", "Loans");
        AddColumn(fixes, "ged.loan_request", "protocol_request_id", "uuid null", "Loans");
        AddColumn(fixes, "ged.protocol_request", "tenant_id", "uuid null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request", "protocol_no", "text null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request", "requester_user_id", "uuid null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request", "requester_name", "text null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request", "requester_sector_id", "uuid null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request", "requester_sector_name", "text null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request", "assigned_sector_id", "uuid null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request", "assigned_sector_name", "text null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request", "assigned_user_id", "uuid null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request", "assigned_user_name", "text null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request", "title", "text not null default 'Solicitação de Protocolo'", "Protocolo");
        AddColumn(fixes, "ged.protocol_request", "description", "text null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request", "priority", "text not null default 'NORMAL'", "Protocolo");
        AddColumn(fixes, "ged.protocol_request", "status", "text not null default 'REQUESTED'", "Protocolo");
        AddColumn(fixes, "ged.protocol_request", "due_at", "timestamptz null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request", "requested_at", "timestamptz not null default now()", "Protocolo");
        AddColumn(fixes, "ged.protocol_request", "updated_at", "timestamptz null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request", "finished_at", "timestamptz null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request", "reg_status", "char(1) not null default 'A'", "Protocolo");
        AddColumn(fixes, "ged.protocol_request", "correlation_id", "text null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request", "created_at", "timestamptz not null default now()", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_item", "tenant_id", "uuid null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_item", "protocol_request_id", "uuid null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_item", "document_id", "uuid null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_item", "document_version_id", "uuid null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_item", "is_manual", "boolean not null default false", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_item", "reference_code", "text null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_item", "description", "text null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_item", "document_type", "text null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_item", "patient_name", "text null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_item", "medical_record_number", "text null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_item", "box_code", "text null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_item", "physical_location", "text null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_item", "notes", "text null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_item", "created_at", "timestamptz not null default now()", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_item", "reg_status", "char(1) not null default 'A'", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_attachment", "tenant_id", "uuid null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_attachment", "protocol_request_id", "uuid null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_attachment", "file_name", "text null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_attachment", "content_type", "text null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_attachment", "size_bytes", "bigint null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_attachment", "storage_path", "text null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_attachment", "uploaded_by", "uuid null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_attachment", "uploaded_by_name", "text null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_attachment", "uploaded_at", "timestamptz not null default now()", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_attachment", "reg_status", "char(1) not null default 'A'", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_history", "tenant_id", "uuid null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_history", "protocol_request_id", "uuid null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_history", "old_status", "text null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_history", "new_status", "text null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_history", "action", "text not null default 'INFO'", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_history", "user_id", "uuid null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_history", "user_name", "text null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_history", "sector_id", "uuid null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_history", "sector_name", "text null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_history", "reason", "text null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_history", "internal_notes", "text null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_history", "metadata_json", "jsonb not null default '{}'::jsonb", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_history", "correlation_id", "text null", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_history", "created_at", "timestamptz not null default now()", "Protocolo");
        AddColumn(fixes, "ged.protocol_request_history", "reg_status", "char(1) not null default 'A'", "Protocolo");

        AddColumn(fixes, "ged.loan_request_history", "tenant_id", "uuid null", "Loans History");
        AddColumn(fixes, "ged.loan_request_history", "loan_request_id", "uuid null", "Loans History");
        AddColumn(fixes, "ged.loan_request_history", "action", "text not null default 'INFO'", "Loans History");
        AddColumn(fixes, "ged.loan_request_history", "old_status", "text null", "Loans History");
        AddColumn(fixes, "ged.loan_request_history", "new_status", "text null", "Loans History");
        AddColumn(fixes, "ged.loan_request_history", "user_id", "uuid null", "Loans History");
        AddColumn(fixes, "ged.loan_request_history", "user_name", "text null", "Loans History");
        AddColumn(fixes, "ged.loan_request_history", "sector_id", "uuid null", "Loans History");
        AddColumn(fixes, "ged.loan_request_history", "sector_name", "text null", "Loans History");
        AddColumn(fixes, "ged.loan_request_history", "reason", "text null", "Loans History");
        AddColumn(fixes, "ged.loan_request_history", "internal_notes", "text null", "Loans History");
        AddColumn(fixes, "ged.loan_request_history", "metadata_json", "jsonb not null default '{}'::jsonb", "Loans History");
        AddColumn(fixes, "ged.loan_request_history", "correlation_id", "text null", "Loans History");
        AddColumn(fixes, "ged.loan_request_history", "created_at", "timestamptz not null default now()", "Loans History");
        AddColumn(fixes, "ged.loan_request_history", "reg_status", "char(1) not null default 'A'", "Loans History");
        AddColumn(fixes, "ged.document_quality_run", "tenant_id", "uuid null", "Qualidade Documental");
        AddColumn(fixes, "ged.document_quality_run", "started_at_utc", "timestamptz not null default now()", "Qualidade Documental");
        AddColumn(fixes, "ged.document_quality_run", "status", "text not null default 'STARTED'", "Qualidade Documental");
        AddColumn(fixes, "ged.document_quality_run", "total_documents", "int not null default 0", "Qualidade Documental");
        AddColumn(fixes, "ged.document_quality_run", "excellent_count", "int not null default 0", "Qualidade Documental");
        AddColumn(fixes, "ged.document_quality_run", "good_count", "int not null default 0", "Qualidade Documental");
        AddColumn(fixes, "ged.document_quality_run", "warning_count", "int not null default 0", "Qualidade Documental");
        AddColumn(fixes, "ged.document_quality_run", "critical_count", "int not null default 0", "Qualidade Documental");
        AddColumn(fixes, "ged.document_quality_run", "failed_count", "int not null default 0", "Qualidade Documental");
        AddColumn(fixes, "ged.document_quality_result", "tenant_id", "uuid null", "Qualidade Documental");
        AddColumn(fixes, "ged.document_quality_result", "document_id", "uuid null", "Qualidade Documental");
        AddColumn(fixes, "ged.document_quality_result", "quality_score", "int not null default 0", "Qualidade Documental");
        AddColumn(fixes, "ged.document_quality_result", "quality_status", "text not null default 'Não analisado'", "Qualidade Documental");
        AddColumn(fixes, "ged.document_quality_result", "has_ocr", "boolean not null default false", "Qualidade Documental");
        AddColumn(fixes, "ged.document_quality_result", "has_ocr_error", "boolean not null default false", "Qualidade Documental");
        AddColumn(fixes, "ged.document_quality_result", "has_classification", "boolean not null default false", "Qualidade Documental");
        AddColumn(fixes, "ged.document_quality_result", "has_document_type", "boolean not null default false", "Qualidade Documental");
        AddColumn(fixes, "ged.document_quality_result", "has_required_metadata", "boolean not null default false", "Qualidade Documental");
        AddColumn(fixes, "ged.document_quality_result", "is_partial_document", "boolean not null default false", "Qualidade Documental");
        AddColumn(fixes, "ged.document_quality_result", "is_partial_incomplete", "boolean not null default false", "Qualidade Documental");
        AddColumn(fixes, "ged.document_quality_result", "is_ready_to_consolidate", "boolean not null default false", "Qualidade Documental");
        AddColumn(fixes, "ged.document_quality_result", "is_consolidated", "boolean not null default false", "Qualidade Documental");
        AddColumn(fixes, "ged.document_quality_result", "has_possible_duplicate", "boolean not null default false", "Qualidade Documental");
        AddColumn(fixes, "ged.document_quality_result", "has_lgpd_risk", "boolean not null default false", "Qualidade Documental");
        AddColumn(fixes, "ged.document_quality_result", "issues_json", "jsonb not null default '[]'::jsonb", "Qualidade Documental");
        AddColumn(fixes, "ged.document_quality_result", "recommendations_json", "jsonb not null default '[]'::jsonb", "Qualidade Documental");
        AddColumn(fixes, "ged.document_quality_result", "analyzed_at_utc", "timestamptz not null default now()", "Qualidade Documental");
    }

    private static void AddIndexFixes(List<SchemaFixDto> fixes)
    {
        fixes.Add(Index("GED_INDEX_DOCUMENT_TENANT_REG_STATUS", "ged.ix_document_tenant_reg_status", "GED soft delete", "Cria índice de filtro por tenant/status lógico.", """
do $$
begin
    if exists (select 1 from information_schema.columns where table_schema='ged' and table_name='document' and column_name='reg_status') then
        execute 'create index if not exists ix_document_tenant_reg_status on ged.document(tenant_id, reg_status)';
    end if;
end $$;
"""));
        fixes.Add(Index("GED_INDEX_DOCUMENT_TENANT_FOLDER_REG_STATUS", "ged.ix_document_tenant_folder_reg_status", "GED soft delete", "Cria índice de navegação por tenant/pasta/status lógico.", """
do $$
begin
    if exists (select 1 from information_schema.columns where table_schema='ged' and table_name='document' and column_name='tenant_id')
       and exists (select 1 from information_schema.columns where table_schema='ged' and table_name='document' and column_name='folder_id')
       and exists (select 1 from information_schema.columns where table_schema='ged' and table_name='document' and column_name='reg_status') then
        execute 'create index if not exists ix_document_tenant_folder_reg_status on ged.document(tenant_id, folder_id, reg_status)';
    end if;
end $$;
"""));
        fixes.Add(Index("GED_INDEX_DOCUMENT_DELETED_AT", "ged.ix_document_deleted_at", "GED soft delete", "Cria índice parcial para auditoria/expurgo futuro de excluídos.", """
do $$
begin
    if exists (select 1 from information_schema.columns where table_schema='ged' and table_name='document' and column_name='deleted_at') then
        execute 'create index if not exists ix_document_deleted_at on ged.document(deleted_at) where deleted_at is not null';
    end if;
end $$;
"""));
        fixes.Add(Index("GED_INDEX_DOCUMENT_TENANT_FOLDER_STATUS", "ged.ix_document_tenant_folder_status", "Performance", "Cria índice de navegação por tenant/pasta/status quando as colunas existem.", """
do $$
begin
    if exists (select 1 from information_schema.columns where table_schema = 'ged' and table_name = 'document' and column_name = 'status') then
        execute 'create index if not exists ix_document_tenant_folder_status on ged.document(tenant_id, folder_id, status)';
    elsif exists (select 1 from information_schema.columns where table_schema = 'ged' and table_name = 'document' and column_name = 'reg_status') then
        execute 'create index if not exists ix_document_tenant_folder_reg_status on ged.document(tenant_id, folder_id, reg_status)';
    end if;
end $$;
"""));
        fixes.Add(Index("GED_INDEX_DOCUMENT_CURRENT_VERSION", "ged.ix_document_current_version", "Performance", "Cria índice de versão atual do documento.", "create index if not exists ix_document_current_version on ged.document(current_version_id) where current_version_id is not null;"));
        fixes.Add(Index("GED_INDEX_DOCUMENT_VERSION_DOCUMENT_CURRENT", "ged.ix_document_version_document_current", "Performance", "Cria índice compatível para resolução da versão atual sem exigir document_version.is_current.", """
do $$
begin
    if exists (
        select 1 from information_schema.columns
        where table_schema='ged'
          and table_name='document_version'
          and column_name='is_current'
    ) then
        execute 'create index if not exists ix_document_version_document_current on ged.document_version(document_id, is_current)';
    elsif exists (
        select 1 from information_schema.columns
        where table_schema='ged'
          and table_name='document'
          and column_name='current_version_id'
    ) then
        execute 'create index if not exists ix_document_current_version on ged.document(current_version_id) where current_version_id is not null';
    else
        raise notice 'GED_INDEX_DOCUMENT_VERSION_DOCUMENT_CURRENT skipped: document.current_version_id não existe.';
    end if;
end $$;
"""));
        fixes.Add(Index("GED_INDEX_DOCUMENT_VERSION_PARTIAL_GROUP_ID", "ged.ix_document_version_partial_group_id", "Performance", "Cria índice para agrupamento de documentos fracionados.", "create index if not exists ix_document_version_partial_group_id on ged.document_version(partial_group_id);"));
        fixes.Add(Index("GED_INDEX_DOCUMENT_VERSION_PARTIAL_STATUS", "ged.ix_document_version_partial_status", "Performance", "Cria índice para status parcial.", "create index if not exists ix_document_version_partial_status on ged.document_version(partial_status);"));
        fixes.Add(Index("GED_INDEX_DOCUMENT_VERSION_UPLOADED_AT_UTC", "ged.ix_document_version_uploaded_at_utc", "Performance", "Cria índice para ordenação por upload.", "create index if not exists ix_document_version_uploaded_at_utc on ged.document_version(uploaded_at_utc);"));
        fixes.Add(Index("GED_INDEX_DOCUMENT_SEARCH_TENANT_VERSION", "ged.ix_ged_document_search_tenant_version", "Performance", "Cria índice de busca OCR por tenant e coluna documental compatível detectada dinamicamente.", DocumentSearchTenantVersionIndexSql, [
            new SchemaObjectDependency { Type = "Table", Schema = "ged", Table = "document_search" },
            new SchemaObjectDependency { Type = "Column", Schema = "ged", Table = "document_search", Column = "tenant_id" },
            new SchemaObjectDependency { Type = "Column", Schema = "ged", Table = "document_search", Column = "document_version_id", Required = false },
            new SchemaObjectDependency { Type = "Column", Schema = "ged", Table = "document_search", Column = "version_id", Required = false },
            new SchemaObjectDependency { Type = "Column", Schema = "ged", Table = "document_search", Column = "document_id", Required = false }
        ]));
        fixes.Add(Index("GED_INDEX_UPLOAD_SESSION_TENANT_USER_STATUS", "ged.ix_upload_session_tenant_user_status", "Performance", "Cria índice de sessões chunked por tenant/usuário/status.", "create index if not exists ix_upload_session_tenant_user_status on ged.upload_session(tenant_id, user_id, status);"));
        fixes.Add(Index("GED_INDEX_UPLOAD_SESSION_CHUNK_SESSION", "ged.ix_upload_session_chunk_session", "Performance", "Cria índice dos chunks por sessão.", "create index if not exists ix_upload_session_chunk_session on ged.upload_session_chunk(session_id, chunk_index);"));
        fixes.Add(Index("GED_INDEX_OCR_AUTO_SCHEDULE_RUN_STATUS", "ged.ix_ocr_auto_schedule_run_status", "OCR Auto Schedule", "Cria índice por tenant/status do agendamento automático de OCR.", "create index if not exists ix_ocr_auto_schedule_run_status on ged.ocr_auto_schedule_run(tenant_id, status);"));
        fixes.Add(Index("GED_INDEX_LOAN_REQUEST_ITEM_LOAN_REQUEST", "ged.ix_loan_request_item_loan_request", "Loans", "Cria índice dos itens por solicitação de empréstimo.", "create index if not exists ix_loan_request_item_loan_request on ged.loan_request_item(loan_request_id);"));
        fixes.Add(Index("GED_INDEX_LOAN_REQUEST_ITEM_DOCUMENT", "ged.ix_loan_request_item_document", "Loans", "Cria índice dos itens por documento GED.", "create index if not exists ix_loan_request_item_document on ged.loan_request_item(document_id);"));
        fixes.Add(Index("GED_INDEX_LOAN_REQUEST_ITEM_MANUAL", "ged.ix_loan_request_item_manual", "Loans", "Cria índice dos itens manuais/físicos.", "create index if not exists ix_loan_request_item_manual on ged.loan_request_item(is_manual);"));
        fixes.Add(Index("GED_INDEX_LOAN_REQUEST_HISTORY_TENANT_LOAN_CREATED", "ged.ix_loan_request_history_tenant_loan_created", "Loans History", "Cria índice do histórico rico por solicitação/data.", "create index if not exists ix_loan_request_history_tenant_loan_created on ged.loan_request_history(tenant_id, loan_request_id, created_at desc);"));
        fixes.Add(Index("GED_INDEX_LOAN_REQUEST_HISTORY_TENANT_ACTION", "ged.ix_loan_request_history_tenant_action", "Loans History", "Cria índice do histórico rico por ação.", "create index if not exists ix_loan_request_history_tenant_action on ged.loan_request_history(tenant_id, action);"));
        fixes.Add(Index("GED_INDEX_LOAN_REQUEST_HISTORY_TENANT_CREATED", "ged.ix_loan_request_history_tenant_created", "Loans History", "Cria índice do histórico rico por data.", "create index if not exists ix_loan_request_history_tenant_created on ged.loan_request_history(tenant_id, created_at desc);"));
        fixes.Add(Index("GED_INDEX_LOAN_REQUEST_HISTORY_TENANT_USER", "ged.ix_loan_request_history_tenant_user", "Loans History", "Cria índice do histórico rico por usuário.", "create index if not exists ix_loan_request_history_tenant_user on ged.loan_request_history(tenant_id, user_id);"));
        fixes.Add(Index("GED_INDEX_PROTOCOL_REQUEST_TENANT_PROTOCOL_NO", "ged.ux_protocol_request_tenant_protocol_no", "Protocolo", "Cria índice único por tenant/número de protocolo.", "create unique index if not exists ux_protocol_request_tenant_protocol_no on ged.protocol_request(tenant_id, protocol_no);"));
        fixes.Add(Index("GED_INDEX_PROTOCOL_REQUEST_TENANT_STATUS", "ged.ix_protocol_request_tenant_status", "Protocolo", "Cria índice por tenant/status.", "create index if not exists ix_protocol_request_tenant_status on ged.protocol_request(tenant_id, status);"));
        fixes.Add(Index("GED_INDEX_PROTOCOL_REQUEST_TENANT_REQUESTER", "ged.ix_protocol_request_tenant_requester", "Protocolo", "Cria índice por tenant/solicitante.", "create index if not exists ix_protocol_request_tenant_requester on ged.protocol_request(tenant_id, requester_user_id);"));
        fixes.Add(Index("GED_INDEX_PROTOCOL_REQUEST_TENANT_ASSIGNED_SECTOR", "ged.ix_protocol_request_tenant_assigned_sector", "Protocolo", "Cria índice por tenant/setor atribuído.", "create index if not exists ix_protocol_request_tenant_assigned_sector on ged.protocol_request(tenant_id, assigned_sector_id);"));
        fixes.Add(Index("GED_INDEX_PROTOCOL_REQUEST_TENANT_ASSIGNED_USER", "ged.ix_protocol_request_tenant_assigned_user", "Protocolo", "Cria índice por tenant/usuário atribuído.", "create index if not exists ix_protocol_request_tenant_assigned_user on ged.protocol_request(tenant_id, assigned_user_id);"));
        fixes.Add(Index("GED_INDEX_PROTOCOL_REQUEST_TENANT_REQUESTED_AT", "ged.ix_protocol_request_tenant_requested_at", "Protocolo", "Cria índice por tenant/data solicitada.", "create index if not exists ix_protocol_request_tenant_requested_at on ged.protocol_request(tenant_id, requested_at desc);"));
        fixes.Add(Index("GED_INDEX_PROTOCOL_REQUEST_ITEM_PROTOCOL", "ged.ix_protocol_request_item_protocol", "Protocolo", "Cria índice dos itens por protocolo.", "create index if not exists ix_protocol_request_item_protocol on ged.protocol_request_item(tenant_id, protocol_request_id);"));
        fixes.Add(Index("GED_INDEX_PROTOCOL_REQUEST_ATTACHMENT_PROTOCOL", "ged.ix_protocol_request_attachment_protocol", "Protocolo", "Cria índice dos anexos por protocolo.", "create index if not exists ix_protocol_request_attachment_protocol on ged.protocol_request_attachment(tenant_id, protocol_request_id);"));
        fixes.Add(Index("GED_INDEX_PROTOCOL_REQUEST_HISTORY_PROTOCOL_CREATED", "ged.ix_protocol_request_history_protocol_created", "Protocolo", "Cria índice do histórico por protocolo/data.", "create index if not exists ix_protocol_request_history_protocol_created on ged.protocol_request_history(tenant_id, protocol_request_id, created_at desc);"));
        fixes.Add(Index("GED_INDEX_LOAN_REQUEST_PROTOCOL_REQUEST", "ged.ix_loan_request_protocol_request", "Loans", "Cria índice do vínculo entre empréstimo e protocolo.", "create index if not exists ix_loan_request_protocol_request on ged.loan_request(tenant_id, protocol_request_id);"));

        fixes.Add(Index("GED_INDEX_APP_AUDIT_LOG_TENANT_CREATED", "ged.ix_app_audit_log_tenant_created", "Performance", "Cria índice de auditoria por tenant/data.", "create index if not exists ix_app_audit_log_tenant_created on ged.app_audit_log(tenant_id, created_at desc);"));
        fixes.Add(Index("GED_INDEX_APP_AUDIT_LOG_USER_CREATED", "ged.ix_app_audit_log_user_created", "Performance", "Cria índice de auditoria por usuário/data.", "create index if not exists ix_app_audit_log_user_created on ged.app_audit_log(user_id, created_at desc);"));
        fixes.Add(Index("GED_INDEX_APP_AUDIT_LOG_ACTION_CREATED", "ged.ix_app_audit_log_action_created", "Performance", "Cria índice de auditoria por ação/data.", "create index if not exists ix_app_audit_log_action_created on ged.app_audit_log(action, created_at desc);"));
        fixes.Add(Index("GED_INDEX_APP_AUDIT_LOG_CORRELATION", "ged.ix_app_audit_log_correlation", "Performance", "Cria índice de auditoria por correlationId.", "create index if not exists ix_app_audit_log_correlation on ged.app_audit_log(correlation_id);"));
        fixes.Add(Index("GED_INDEX_UPLOAD_DUPLICATE_DECISION_TENANT_BATCH", "ged.ix_upload_duplicate_decision_tenant_batch", "Upload batch", "Cria índice para auditoria de decisões de duplicidade por lote.", "create index if not exists ix_upload_duplicate_decision_tenant_batch on ged.upload_duplicate_decision(tenant_id, batch_id, decided_at desc);"));
        fixes.Add(Index("GED_INDEX_UPLOAD_BATCH_ITEM_TENANT_DUPLICATE", "ged.ix_upload_batch_item_tenant_duplicate", "Upload batch", "Cria índice para vínculo de possível duplicidade.", "create index if not exists ix_upload_batch_item_tenant_duplicate on ged.upload_batch_item(tenant_id, duplicate_of_document_id);"));
        fixes.Add(Index("GED_INDEX_DOCUMENT_QUALITY_RESULT_TENANT_DOCUMENT_ANALYZED", "ged.ix_document_quality_result_tenant_document_analyzed", "Qualidade Documental", "Cria índice por tenant/documento/última análise.", "create index if not exists ix_document_quality_result_tenant_document_analyzed on ged.document_quality_result(tenant_id, document_id, analyzed_at_utc desc);"));
        fixes.Add(Index("GED_INDEX_DOCUMENT_QUALITY_RESULT_TENANT_STATUS", "ged.ix_document_quality_result_tenant_status", "Qualidade Documental", "Cria índice por tenant/status.", "create index if not exists ix_document_quality_result_tenant_status on ged.document_quality_result(tenant_id, quality_status);"));
        fixes.Add(Index("GED_INDEX_DOCUMENT_QUALITY_RESULT_TENANT_SCORE", "ged.ix_document_quality_result_tenant_score", "Qualidade Documental", "Cria índice por tenant/score.", "create index if not exists ix_document_quality_result_tenant_score on ged.document_quality_result(tenant_id, quality_score);"));
        fixes.Add(Index("GED_INDEX_DOCUMENT_QUALITY_RESULT_TENANT_HAS_OCR", "ged.ix_document_quality_result_tenant_has_ocr", "Qualidade Documental", "Cria índice por tenant/OCR.", "create index if not exists ix_document_quality_result_tenant_has_ocr on ged.document_quality_result(tenant_id, has_ocr);"));
        fixes.Add(Index("GED_INDEX_DOCUMENT_QUALITY_RESULT_TENANT_LGPD", "ged.ix_document_quality_result_tenant_lgpd", "Qualidade Documental", "Cria índice por tenant/risco LGPD.", "create index if not exists ix_document_quality_result_tenant_lgpd on ged.document_quality_result(tenant_id, has_lgpd_risk);"));
        fixes.Add(Index("GED_INDEX_DOCUMENT_QUALITY_RESULT_RUN", "ged.ix_document_quality_result_run", "Qualidade Documental", "Cria índice por execução.", "create index if not exists ix_document_quality_result_run on ged.document_quality_result(run_id);"));
        fixes.Add(Index("GED_INDEX_DOCUMENT_QUALITY_RUN_TENANT_STARTED", "ged.ix_document_quality_run_tenant_started", "Qualidade Documental", "Cria índice de execuções por tenant/data.", "create index if not exists ix_document_quality_run_tenant_started on ged.document_quality_run(tenant_id, started_at_utc desc);"));
        fixes.Add(Index("GED_INDEX_DOCUMENT_QUALITY_RUN_STATUS", "ged.ix_document_quality_run_status", "Qualidade Documental", "Cria índice de execuções por tenant/status.", "create index if not exists ix_document_quality_run_status on ged.document_quality_run(tenant_id, status);"));
    }

    private static SchemaFixDto Table(string id, string objectName, string area, string description, string sql) => new()
    {
        CheckId = id,
        ObjectName = objectName,
        Area = area,
        Description = description,
        FixSql = sql.Trim() + Environment.NewLine,
        CanAutoFix = true,
        RiskLevel = "Low",
        FixType = "Table",
        Dependencies = [new SchemaObjectDependency { Type = "Schema", Schema = "ged" }],
        ScriptName = ConsolidatedScriptName
    };

    private static void AddColumn(List<SchemaFixDto> fixes, string table, string column, string definition, string area)
    {
        fixes.Add(new SchemaFixDto
        {
            CheckId = $"GED_COLUMN_{table.Replace("ged.", string.Empty).ToUpperInvariant()}_{column.ToUpperInvariant()}",
            ObjectName = $"{table}.{column}",
            Area = area,
            Description = $"Adiciona a coluna {table}.{column} de forma idempotente.",
            FixSql = $"alter table {table}\nadd column if not exists {column} {definition};\n",
            CanAutoFix = true,
            RiskLevel = "Low",
            FixType = "Column",
            Dependencies = [new SchemaObjectDependency { Type = "Table", Schema = "ged", Table = table.Replace("ged.", string.Empty, StringComparison.OrdinalIgnoreCase) }],
            ScriptName = ConsolidatedScriptName
        });
    }

    private static SchemaFixDto Index(string id, string objectName, string area, string description, string sql, List<SchemaObjectDependency>? dependencies = null) => new()
    {
        CheckId = id,
        ObjectName = objectName,
        Area = area,
        Description = description,
        FixSql = sql.Trim() + Environment.NewLine,
        CanAutoFix = true,
        RiskLevel = "Low",
        FixType = "Index",
        Dependencies = dependencies ?? BuildIndexDependencies(sql),
        ScriptName = ConsolidatedScriptName
    };

    private static SchemaFixDto Clone(SchemaFixDto fix) => new()
    {
        CheckId = fix.CheckId,
        ObjectName = fix.ObjectName,
        Area = fix.Area,
        FixSql = fix.FixSql,
        CanAutoFix = fix.CanAutoFix,
        RiskLevel = fix.RiskLevel,
        FixType = fix.FixType,
        Description = fix.Description,
        ScriptName = fix.ScriptName,
        Dependencies = fix.Dependencies.Select(d => new SchemaObjectDependency
        {
            Type = d.Type,
            Schema = d.Schema,
            Table = d.Table,
            Column = d.Column,
            Required = d.Required
        }).ToList()
    };

    private static List<SchemaObjectDependency> BuildIndexDependencies(string sql)
    {
        if (sql.Contains("do $$", StringComparison.OrdinalIgnoreCase))
            return [new SchemaObjectDependency { Type = "Schema", Schema = "ged" }];

        var normalized = sql.Replace("\n", " ", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal);
        var match = System.Text.RegularExpressions.Regex.Match(normalized, @"on\s+ged\.(?<table>[a-zA-Z0-9_]+)\s*\((?<columns>[^\)]+)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success)
            return [new SchemaObjectDependency { Type = "Schema", Schema = "ged" }];

        var table = match.Groups["table"].Value;
        var dependencies = new List<SchemaObjectDependency> { new() { Type = "Table", Schema = "ged", Table = table } };
        foreach (var column in match.Groups["columns"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var cleanColumn = column.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim('"');
            if (!string.IsNullOrWhiteSpace(cleanColumn) && !cleanColumn.Contains('('))
                dependencies.Add(new SchemaObjectDependency { Type = "Column", Schema = "ged", Table = table, Column = cleanColumn });
        }

        return dependencies;
    }

    private static bool IsDocumentSearchTenantVersionCheck(string checkId)
        => string.Equals(NormalizeId(checkId), "GED_INDEX_DOCUMENT_SEARCH_TENANT_VERSION", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeId(string id) => (id ?? string.Empty).Trim().Replace('.', '_').Replace('-', '_').ToUpperInvariant();
}
