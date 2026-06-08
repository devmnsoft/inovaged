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
            Table("GED_TABLE_APP_AUDIT_LOG", "ged.app_audit_log", "Audit", "Cria a tabela de auditoria técnica da aplicação.", """
create extension if not exists pgcrypto;

create table if not exists ged.app_audit_log (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid null,
    user_id uuid null,
    user_name text null,
    action text not null,
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
""")
        };

        AddColumnFixes(fixes);
        AddIndexFixes(fixes);
        return fixes.ToDictionary(f => NormalizeId(f.CheckId), StringComparer.OrdinalIgnoreCase);
    }

    private static void AddColumnFixes(List<SchemaFixDto> fixes)
    {
        AddColumn(fixes, "ged.document_version", "partial_group_id", "uuid null", "GED partial documents");
        AddColumn(fixes, "ged.document_version", "partial_part_number", "int null", "GED partial documents");
        AddColumn(fixes, "ged.document_version", "partial_total_parts", "int null", "GED partial documents");
        AddColumn(fixes, "ged.document_version", "partial_status", "text not null default 'NOT_PARTIAL'", "GED partial documents");
        AddColumn(fixes, "ged.document_version", "is_partial_document", "boolean not null default false", "GED partial documents");
        AddColumn(fixes, "ged.document_version", "consolidated_version_id", "uuid null", "GED partial documents");
        AddColumn(fixes, "ged.document_version", "uploaded_at_utc", "timestamptz null", "GED versions");
        AddColumn(fixes, "ged.upload_batch_item", "upload_session_id", "uuid null", "Upload batch");
        AddColumn(fixes, "ged.upload_session", "tenant_id", "uuid null", "Upload chunks");
        AddColumn(fixes, "ged.upload_session", "total_chunks", "int not null default 0", "Upload chunks");
        AddColumn(fixes, "ged.upload_session_chunk", "session_id", "uuid null", "Upload chunks");
        AddColumn(fixes, "ged.upload_session_chunk", "chunk_index", "int null", "Upload chunks");
        AddColumn(fixes, "ged.app_audit_log", "created_at", "timestamptz not null default now()", "SystemLogs");
        AddColumn(fixes, "ged.app_audit_log", "user_name", "text null", "SystemLogs");
        AddColumn(fixes, "ged.audit_log", "created_at", "timestamptz null", "SystemLogs");
        AddColumn(fixes, "ged.audit_log", "user_name", "text null", "SystemLogs");
    }

    private static void AddIndexFixes(List<SchemaFixDto> fixes)
    {
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
        fixes.Add(Index("GED_INDEX_DOCUMENT_CURRENT_VERSION", "ged.ix_document_current_version", "Performance", "Cria índice de versão atual do documento.", "create index if not exists ix_document_current_version on ged.document(current_version_id);"));
        fixes.Add(Index("GED_INDEX_DOCUMENT_VERSION_DOCUMENT_CURRENT", "ged.ix_document_version_document_current", "Performance", "Cria índice alternativo de versão atual por documento.", "create index if not exists ix_document_version_document_current on ged.document_version(document_id, id);"));
        fixes.Add(Index("GED_INDEX_DOCUMENT_VERSION_PARTIAL_GROUP_ID", "ged.ix_document_version_partial_group_id", "Performance", "Cria índice para agrupamento de documentos fracionados.", "create index if not exists ix_document_version_partial_group_id on ged.document_version(partial_group_id);"));
        fixes.Add(Index("GED_INDEX_DOCUMENT_VERSION_PARTIAL_STATUS", "ged.ix_document_version_partial_status", "Performance", "Cria índice para status parcial.", "create index if not exists ix_document_version_partial_status on ged.document_version(partial_status);"));
        fixes.Add(Index("GED_INDEX_DOCUMENT_VERSION_UPLOADED_AT_UTC", "ged.ix_document_version_uploaded_at_utc", "Performance", "Cria índice para ordenação por upload.", "create index if not exists ix_document_version_uploaded_at_utc on ged.document_version(uploaded_at_utc);"));
        fixes.Add(Index("GED_INDEX_DOCUMENT_SEARCH_TENANT_VERSION", "ged.ix_ged_document_search_tenant_version", "Performance", "Cria índice de busca OCR por tenant e coluna documental compatível detectada dinamicamente.", DocumentSearchTenantVersionIndexSql));
        fixes.Add(Index("GED_INDEX_UPLOAD_SESSION_TENANT_USER_STATUS", "ged.ix_upload_session_tenant_user_status", "Performance", "Cria índice de sessões chunked por tenant/usuário/status.", "create index if not exists ix_upload_session_tenant_user_status on ged.upload_session(tenant_id, user_id, status);"));
        fixes.Add(Index("GED_INDEX_UPLOAD_SESSION_CHUNK_SESSION", "ged.ix_upload_session_chunk_session", "Performance", "Cria índice dos chunks por sessão.", "create index if not exists ix_upload_session_chunk_session on ged.upload_session_chunk(session_id, chunk_index);"));
        fixes.Add(Index("GED_INDEX_APP_AUDIT_LOG_TENANT_CREATED", "ged.ix_app_audit_log_tenant_created", "Performance", "Cria índice de auditoria por tenant/data.", "create index if not exists ix_app_audit_log_tenant_created on ged.app_audit_log(tenant_id, created_at desc);"));
        fixes.Add(Index("GED_INDEX_APP_AUDIT_LOG_USER_CREATED", "ged.ix_app_audit_log_user_created", "Performance", "Cria índice de auditoria por usuário/data.", "create index if not exists ix_app_audit_log_user_created on ged.app_audit_log(user_id, created_at desc);"));
        fixes.Add(Index("GED_INDEX_APP_AUDIT_LOG_ACTION_CREATED", "ged.ix_app_audit_log_action_created", "Performance", "Cria índice de auditoria por ação/data.", "create index if not exists ix_app_audit_log_action_created on ged.app_audit_log(action, created_at desc);"));
        fixes.Add(Index("GED_INDEX_APP_AUDIT_LOG_CORRELATION", "ged.ix_app_audit_log_correlation", "Performance", "Cria índice de auditoria por correlationId.", "create index if not exists ix_app_audit_log_correlation on ged.app_audit_log(correlation_id);"));
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
            ScriptName = ConsolidatedScriptName
        });
    }

    private static SchemaFixDto Index(string id, string objectName, string area, string description, string sql) => new()
    {
        CheckId = id,
        ObjectName = objectName,
        Area = area,
        Description = description,
        FixSql = sql.Trim() + Environment.NewLine,
        CanAutoFix = true,
        RiskLevel = "Low",
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
        Description = fix.Description,
        ScriptName = fix.ScriptName,
        Dependencies = fix.Dependencies.ToArray()
    };

    private static bool IsDocumentSearchTenantVersionCheck(string checkId)
        => string.Equals(NormalizeId(checkId), "GED_INDEX_DOCUMENT_SEARCH_TENANT_VERSION", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeId(string id) => (id ?? string.Empty).Trim().Replace('.', '_').Replace('-', '_').ToUpperInvariant();
}
