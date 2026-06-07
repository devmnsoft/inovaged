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
        "ged.document", "ged.document_version", "ged.folder", "ged.document_search", "ged.ocr_job", "ged.audit_log",
        "ged.app_audit_log", "ged.app_user", "ged.upload_batch", "ged.upload_batch_item", "ged.upload_session",
        "ged.upload_session_chunk", "ged.document_partial_part", "ged.folder_virtual_map"
    ];

    private static readonly (string Table, string Column, string Area)[] RequiredColumns =
    [
        ("document", "id", "GED base"), ("document", "tenant_id", "GED base"), ("document", "folder_id", "GED folders"),
        ("document", "current_version_id", "GED versions"), ("document", "created_at", "GED base"),
        ("document_version", "uploaded_at_utc", "GED versions"), ("document_version", "is_partial_document", "GED partial documents"),
        ("document_version", "partial_group_id", "GED partial documents"), ("document_version", "partial_part_number", "GED partial documents"),
        ("document_version", "partial_total_parts", "GED partial documents"), ("document_version", "partial_status", "GED partial documents"),
        ("document_version", "consolidated_version_id", "GED partial documents"), ("document_search", "ocr_text", "OCR"),
        ("ocr_job", "document_version_id", "OCR"), ("ocr_job", "status", "OCR"), ("ocr_job", "requested_at", "OCR"),
        ("ocr_job", "finished_at", "OCR"), ("ocr_job", "invalidate_digital_signatures", "OCR"),
        ("upload_batch", "tenant_id", "Upload batch"), ("upload_batch", "status", "Upload batch"),
        ("upload_batch", "requested_folder_id", "Upload batch"), ("upload_batch_item", "batch_id", "Upload batch"),
        ("upload_batch_item", "upload_session_id", "Upload batch"), ("upload_batch_item", "status", "Upload batch"),
        ("upload_session", "tenant_id", "Upload chunks"), ("upload_session", "total_chunks", "Upload chunks"),
        ("upload_session_chunk", "session_id", "Upload chunks"), ("upload_session_chunk", "chunk_index", "Upload chunks"),
        ("audit_log", "created_at", "SystemLogs"), ("audit_log", "user_name", "SystemLogs"),
        ("app_audit_log", "created_at", "SystemLogs"), ("app_audit_log", "user_name", "SystemLogs")
    ];

    private static readonly (string Name, string[] Alternatives, string Message)[] RecommendedIndexes =
    [
        ("ix_document_tenant_folder_status", ["ix_document_tenant_folder_reg_status"], "Índice de navegação por tenant/pasta/status."),
        ("ix_document_version_document_current", ["ix_document_current_version"], "Índice de resolução da versão atual."),
        ("ix_document_version_partial_group_id", [], "Índice para documentos fracionados por grupo."),
        ("ix_document_version_partial_status", [], "Índice para filtro por status parcial."),
        ("ix_document_version_uploaded_at_utc", [], "Índice para ordenação por upload."),
        ("ix_upload_batch_tenant_status", [], "Índice do upload em lote."),
        ("ix_upload_batch_item_status", [], "Índice dos itens de upload em lote."),
        ("ix_upload_batch_item_upload_session", [], "Índice de vínculo entre lote e sessão chunked."),
        ("ix_upload_session_tenant_user_status", [], "Índice de sessões chunked por usuário/status."),
        ("ix_upload_session_chunk_session", [], "Índice dos chunks por sessão."),
        ("ix_ocr_job_tenant_version_status", [], "Índice da fila OCR por versão/status."),
        ("ix_ged_document_search_tenant_version", [], "Índice de busca OCR por versão.")
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
                AddCheck(report, "GED", table, "Tabela", "Crítico", ok, ok ? "Tabela encontrada." : "Tabela crítica ausente.", $"Execute {ConsolidationMigration}.");
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
                AddCheck(report, area, objectName, "Coluna", "Crítico", ok, ok ? "Coluna encontrada." : "Coluna crítica ausente.", $"Execute {ConsolidationMigration}.");
                if (!ok) report.MissingColumns.Add(objectName);
            }

            var existingIndexes = (await conn.QueryAsync<string>(new CommandDefinition(@"
select indexname
from pg_indexes
where schemaname = 'ged';", cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var (name, alternatives, message) in RecommendedIndexes)
            {
                var foundName = existingIndexes.Contains(name) ? name : alternatives.FirstOrDefault(existingIndexes.Contains);
                var ok = !string.IsNullOrWhiteSpace(foundName);
                AddCheck(report, "Performance", $"ged.{name}", "Índice", "Recomendado", ok,
                    ok ? $"{message} OK ({foundName})." : $"{message} Ausente ou não aplicável por diferença de colunas no ambiente.",
                    $"Use o botão 'Copiar SQL de correção' e execute {ConsolidationMigration}; índices opcionais são criados somente quando as colunas existem.");
            }

            var existingEnums = (await conn.QueryAsync<string>(new CommandDefinition(@"
select n.nspname || '.' || t.typname
from pg_type t
join pg_namespace n on n.oid = t.typnamespace
where n.nspname = 'ged' and t.typname in ('document_status_enum','document_visibility_enum','ocr_status_enum','preview_processing_status');", cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var enumName in new[] { "ged.document_status_enum", "ged.document_visibility_enum", "ged.ocr_status_enum" })
            {
                var ok = existingEnums.Contains(enumName);
                AddCheck(report, "Enums", enumName, "Enum", "Crítico", ok, ok ? "Enum encontrado." : "Enum crítico ausente.", "Aplique as migrations base e a consolidação do GED.");
            }

            var migrationRegistered = existingTables.Contains("ged.schema_migration_history") || await conn.ExecuteScalarAsync<bool>(new CommandDefinition(@"
select exists (
    select 1
    from information_schema.tables
    where table_schema = 'public'
      and table_name in ('schema_migrations','__efmigrationshistory','migration_history')
);", cancellationToken: ct));
            AddCheck(report, "Migrations", "ged.schema_migration_history", "Diagnóstico", "Opcional", migrationRegistered,
                migrationRegistered ? "Histórico de migrations encontrado." : "Histórico padronizado ainda não detectado.",
                "O script consolidado cria ged.schema_migration_history e registra a aplicação.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao executar diagnóstico de schema do banco.");
            AddCheck(report, "Banco", "Conexão/diagnóstico", "Erro", "Crítico", false, "Falha ao consultar metadados do banco.", "Verifique a connection string e permissões de information_schema/pg_catalog.");
        }

        report.IsHealthy = !report.Checks.Any(c => !c.Success && c.Severity == "Crítico");
        report.Recommendations.Add(report.IsHealthy
            ? "Schema compatível: tabelas e colunas críticas necessárias ao GED/OCR/upload/logs foram encontradas."
            : $"Execute o script consolidado {ConsolidationMigration} ou o master database/apply_all_required_migrations.sql.");
        report.Recommendations.Add("Use 'Copiar SQL de correção' para obter o comando psql recomendado e reabra /SystemHealth/Schema após aplicar.");
        report.Recommendations.Add("Pendências recomendadas são índices/performance; pendências opcionais são histórico/melhorias operacionais.");

        return report;
    }

    private static void AddCheck(SchemaHealthReportDto report, string area, string objectName, string checkType, string severity, bool success, string message, string fixSuggestion)
    {
        report.Checks.Add(new SchemaCheckItemDto
        {
            Area = area,
            ObjectName = objectName,
            CheckType = checkType,
            Severity = severity,
            Success = success,
            Message = message,
            FixSuggestion = success ? string.Empty : fixSuggestion
        });
    }
}
