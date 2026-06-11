using System.Collections.Concurrent;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Operations;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Operations;

public sealed class TableSchemaGuard : ITableSchemaGuard
{
    private static readonly ConcurrentDictionary<string, bool> WarnedModules = new(StringComparer.OrdinalIgnoreCase);
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<TableSchemaGuard> _logger;

    public TableSchemaGuard(IDbConnectionFactory db, ILogger<TableSchemaGuard> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<bool> TableExistsAsync(string schema, string table, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            return await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
select exists (
    select 1
      from information_schema.tables
     where table_schema = @schema and table_name = @table
);
""", new { schema, table }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao verificar tabela {Schema}.{Table}.", schema, table);
            return false;
        }
    }

    public async Task<bool> ColumnExistsAsync(string schema, string table, string column, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            return await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
select exists (
    select 1
      from information_schema.columns
     where table_schema = @schema and table_name = @table and column_name = @column
);
""", new { schema, table, column }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao verificar coluna {Schema}.{Table}.{Column}.", schema, table, column);
            return false;
        }
    }

    public async Task<ModuleSchemaStatus> GetModuleStatusAsync(string moduleName, CancellationToken ct)
    {
        var module = Normalize(moduleName);
        var status = new ModuleSchemaStatus { ModuleName = moduleName };
        var requiredTables = module switch
        {
            "GED" => new[] { "document", "document_version" },
            "OCR" => new[] { "document", "document_version" },
            "PARTIAL" or "PARTIALS" or "DOCUMENTOSPARCIAIS" => new[] { "document", "document_version" },
            "LOANS" => new[] { "loan_request", "loan_request_item" },
            "PROTOCOL" or "PROTOCOLO" => new[] { "protocol_request" },
            "QUALITY" or "QUALIDADE" => new[] { "document_quality_result" },
            "ALERTS" or "ALERTAS" => new[] { "document" },
            _ => Array.Empty<string>()
        };

        foreach (var table in requiredTables)
        {
            if (!await TableExistsAsync("ged", table, ct)) status.MissingTables.Add($"ged.{table}");
        }

        if (status.MissingTables.Count == 0)
        {
            if (module is "LOANS" && !await ColumnExistsAsync("ged", "loan_request", "requester_sector", ct)
                && !await ColumnExistsAsync("ged", "loan_request", "requester_sector_name", ct)
                && !await ColumnExistsAsync("ged", "loan_request", "sector_name", ct)
                && !await ColumnExistsAsync("ged", "loan_request", "requesting_sector", ct))
            {
                status.MissingColumns.Add("ged.loan_request.requester_sector/requester_sector_name/sector_name/requesting_sector");
            }

            if ((module is "GED" or "OCR" or "PARTIAL" or "PARTIALS" or "DOCUMENTOSPARCIAIS")
                && !await ColumnExistsAsync("ged", "document", "type_id", ct)
                && !await ColumnExistsAsync("ged", "document", "classification_id", ct))
            {
                status.MissingColumns.Add("ged.document.type_id/classification_id");
            }
        }

        status.IsReady = status.MissingTables.Count == 0;
        status.SuggestedMigration = module switch
        {
            "PROTOCOL" or "PROTOCOLO" => "database/migrations/2026_06_protocol_module.sql",
            "QUALITY" or "QUALIDADE" => "database/migrations/2026_06_document_quality.sql",
            "LOANS" => "database/migrations/2026_06_loans_history.sql",
            "PARTIAL" or "PARTIALS" or "DOCUMENTOSPARCIAIS" => "database/migrations/2026_06_document_partial_workflow.sql",
            _ => "database/apply_all_required_migrations.sql"
        };
        status.StatusText = status.IsReady
            ? (status.MissingColumns.Count == 0 ? "Configurado" : "Configurado com colunas opcionais ausentes")
            : $"Módulo {moduleName} ainda não configurado.";

        if (!status.IsReady && WarnedModules.TryAdd(module, true))
            _logger.LogWarning("Operations module {Module} not ready. Missing tables: {MissingTables}. Missing columns: {MissingColumns}.", moduleName, string.Join(", ", status.MissingTables), string.Join(", ", status.MissingColumns));

        return status;
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).ToUpperInvariant();
}
