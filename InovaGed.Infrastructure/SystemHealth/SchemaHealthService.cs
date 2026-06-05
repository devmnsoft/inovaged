using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.SystemHealth;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.SystemHealth;

public sealed class SchemaHealthService : ISchemaHealthService
{
    private const string ConsolidationMigration = "database/migrations/2026_06_ged_schema_consolidation.sql";
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<SchemaHealthService> _logger;

    private static readonly string[] RequiredTables =
    [
        "ged.document",
        "ged.document_version",
        "ged.folder",
        "ged.document_search",
        "ged.ocr_job",
        "ged.audit_log",
        "ged.app_audit_log",
        "ged.app_user",
        "ged.upload_batch",
        "ged.upload_batch_item",
        "ged.upload_session",
        "ged.upload_session_chunk",
        "ged.document_partial_part",
        "ged.folder_virtual_map"
    ];

    private static readonly (string Table, string Column, string Area)[] RequiredColumns =
    [
        ("document", "id", "GED base"),
        ("document", "tenant_id", "GED base"),
        ("document", "folder_id", "GED folders"),
        ("document", "current_version_id", "GED versions"),
        ("document", "created_at", "GED base"),
        ("document_version", "uploaded_at_utc", "GED versions"),
        ("document_version", "is_partial_document", "GED partial documents"),
        ("document_version", "partial_group_id", "GED partial documents"),
        ("document_version", "partial_part_number", "GED partial documents"),
        ("document_version", "partial_total_parts", "GED partial documents"),
        ("document_version", "partial_status", "GED partial documents"),
        ("document_version", "consolidated_version_id", "GED partial documents"),
        ("document_search", "ocr_text", "OCR"),
        ("ocr_job", "document_version_id", "OCR"),
        ("ocr_job", "status", "OCR"),
        ("ocr_job", "requested_at", "OCR"),
        ("ocr_job", "finished_at", "OCR"),
        ("ocr_job", "invalidate_digital_signatures", "OCR"),
        ("upload_batch", "tenant_id", "Upload batch"),
        ("upload_batch", "status", "Upload batch"),
        ("upload_batch", "requested_folder_id", "Upload batch"),
        ("upload_batch_item", "batch_id", "Upload batch"),
        ("upload_batch_item", "upload_session_id", "Upload batch"),
        ("upload_batch_item", "status", "Upload batch"),
        ("upload_session", "tenant_id", "Upload chunks"),
        ("upload_session", "total_chunks", "Upload chunks"),
        ("upload_session_chunk", "session_id", "Upload chunks"),
        ("upload_session_chunk", "chunk_index", "Upload chunks"),
        ("audit_log", "created_at", "SystemLogs"),
        ("audit_log", "user_name", "SystemLogs"),
        ("app_audit_log", "created_at", "SystemLogs"),
        ("app_audit_log", "user_name", "SystemLogs")
    ];

    private static readonly string[] ImportantIndexes =
    [
        "ix_document_version_document_current",
        "ix_document_version_partial_group_id",
        "ix_document_version_partial_status",
        "ix_document_version_uploaded_at_utc",
        "ix_document_tenant_folder_status",
        "ix_upload_batch_tenant_status",
        "ix_upload_batch_item_status",
        "ix_upload_session_tenant_user_status",
        "ix_upload_session_chunk_session",
        "ix_ocr_job_tenant_version_status",
        "ix_ged_document_search_tenant_version"
    ];

    public SchemaHealthService(IDbConnectionFactory db, ILogger<SchemaHealthService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<SchemaHealthReportDto> CheckAsync(CancellationToken ct)
    {
        var report = new SchemaHealthReportDto();

        try
        {
            await using var conn = await _db.OpenAsync(ct);

            var existingTables = (await conn.QueryAsync<string>(new CommandDefinition(@"
select table_schema || '.' || table_name
from information_schema.tables
where table_schema = 'ged';", cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var table in RequiredTables)
            {
                var ok = existingTables.Contains(table);
                AddCheck(report, table.Split('.')[0].Equals("ged", StringComparison.OrdinalIgnoreCase) ? "GED" : "Banco", table, "Tabela", ok,
                    ok ? "Tabela encontrada." : "Tabela obrigatória ausente.", $"Execute {ConsolidationMigration}.");
                if (!ok) report.MissingTables.Add(table);
            }

            var existingColumns = (await conn.QueryAsync<string>(new CommandDefinition(@"
select table_name || '.' || column_name
from information_schema.columns
where table_schema = 'ged';", cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var (table, column, area) in RequiredColumns)
            {
                var objectName = $"ged.{table}.{column}";
                var ok = existingColumns.Contains($"{table}.{column}");
                AddCheck(report, area, objectName, "Coluna", ok,
                    ok ? "Coluna encontrada." : "Coluna obrigatória ausente.", $"Execute {ConsolidationMigration}.");
                if (!ok) report.MissingColumns.Add(objectName);
            }

            var existingIndexes = (await conn.QueryAsync<string>(new CommandDefinition(@"
select indexname
from pg_indexes
where schemaname = 'ged';", cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var index in ImportantIndexes)
            {
                var ok = existingIndexes.Contains(index);
                AddCheck(report, "Performance", $"ged.{index}", "Índice", ok,
                    ok ? "Índice encontrado." : "Índice recomendado ausente.", $"Execute {ConsolidationMigration} para recriar índices idempotentes.");
            }

            var enumChecks = await conn.QueryAsync<string>(new CommandDefinition(@"
select n.nspname || '.' || t.typname
from pg_type t
join pg_namespace n on n.oid = t.typnamespace
where n.nspname = 'ged' and t.typname in ('document_status_enum','document_visibility_enum','ocr_status_enum','preview_processing_status');", cancellationToken: ct));
            var existingEnums = enumChecks.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var enumName in new[] { "ged.document_status_enum", "ged.document_visibility_enum", "ged.ocr_status_enum" })
            {
                var ok = existingEnums.Contains(enumName);
                AddCheck(report, "Enums", enumName, "Enum", ok,
                    ok ? "Enum encontrado." : "Enum crítico ausente.", "Aplique as migrations base e a consolidação do GED.");
            }

            var migrationRegistered = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(@"
select exists (
    select 1
    from information_schema.tables
    where table_schema in ('public','ged')
      and table_name in ('schema_migrations','__efmigrationshistory','migration_history')
);", cancellationToken: ct));
            AddCheck(report, "Migrations", "Histórico de migrations", "Diagnóstico", migrationRegistered,
                migrationRegistered ? "Tabela de histórico de migrations encontrada." : "Não há tabela padronizada de histórico de migrations detectada.",
                "Registre os scripts aplicados em produção e use database/apply_all_required_migrations.sql como pacote mínimo.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao executar diagnóstico de schema do banco.");
            AddCheck(report, "Banco", "Conexão/diagnóstico", "Erro", false, "Falha ao consultar metadados do banco.", "Verifique a connection string e permissões de information_schema/pg_catalog.");
        }

        report.IsHealthy = report.Checks.All(c => c.Success || c.CheckType == "Índice" || c.CheckType == "Diagnóstico");
        if (!report.IsHealthy)
        {
            report.Recommendations.Add($"Execute o script consolidado {ConsolidationMigration} ou o master database/apply_all_required_migrations.sql.");
            report.Recommendations.Add("Abra /SystemHealth/Schema após aplicar as migrations e confirme que tabelas/colunas críticas estão OK.");
            report.Recommendations.Add("Para erros PostgreSQL 42703/42P01, aplique as migrations antes de liberar telas críticas.");
        }
        else
        {
            report.Recommendations.Add("Schema mínimo compatível com as consultas críticas do GED.");
        }

        return report;
    }

    private static void AddCheck(SchemaHealthReportDto report, string area, string objectName, string checkType, bool success, string message, string fixSuggestion)
    {
        report.Checks.Add(new SchemaCheckItemDto
        {
            Area = area,
            ObjectName = objectName,
            CheckType = checkType,
            Success = success,
            Message = message,
            FixSuggestion = success ? string.Empty : fixSuggestion
        });
    }
}
