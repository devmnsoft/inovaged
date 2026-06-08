using System.Security.Cryptography;
using System.Text;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Identity;
using InovaGed.Application.SystemHealth;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace InovaGed.Infrastructure.SystemHealth;

public sealed class SchemaRepairService : ISchemaRepairService
{
    private const int CommandTimeoutSeconds = 180;
    private readonly IDbConnectionFactory _db;
    private readonly ISchemaHealthService _schemaHealth;
    private readonly ISchemaFixSqlProvider _fixSqlProvider;
    private readonly IOptions<SchemaRepairOptions> _options;
    private readonly IHostEnvironment _environment;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<SchemaRepairService> _logger;

    public SchemaRepairService(
        IDbConnectionFactory db,
        ISchemaHealthService schemaHealth,
        ISchemaFixSqlProvider fixSqlProvider,
        IOptions<SchemaRepairOptions> options,
        IHostEnvironment environment,
        ICurrentUser currentUser,
        ILogger<SchemaRepairService> logger)
    {
        _db = db;
        _schemaHealth = schemaHealth;
        _fixSqlProvider = fixSqlProvider;
        _options = options;
        _environment = environment;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<SchemaRepairResultDto> ApplyFixAsync(string checkId, string confirmation, Guid userId, CancellationToken ct)
    {
        var correlationId = NewCorrelationId();
        var result = new SchemaRepairResultDto { CheckId = checkId, CorrelationId = correlationId };

        try
        {
            var validation = ValidateApplyRequest(confirmation);
            if (validation is not null)
                return Fail(result, validation);

            var report = await _schemaHealth.CheckAsync(ct);
            var fix = (await _fixSqlProvider.GetFixesAsync(report, ct))
                .FirstOrDefault(f => string.Equals(f.CheckId, checkId, StringComparison.OrdinalIgnoreCase));

            if (fix is null)
                return Fail(result, "Correção não encontrada para falha atual do schema ou falha já resolvida.");

            if (!fix.CanAutoFix)
                return Fail(result, "Correção não marcada como automática/segura.");

            var safety = ValidateWhitelistedSql(fix);
            if (safety is not null)
                return Fail(result, safety);

            var items = await ExecuteFixesAsync([fix], "SCHEMA_FIX_APPLY", userId, correlationId, ct);
            PopulateResultCounts(result, items);
            result.Success = result.FailedCount == 0;
            result.Message = BuildRepairMessage(result, singleFix: true);
            result.Report = await _schemaHealth.CheckAsync(ct);
            return result;
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "Erro PostgreSQL ao aplicar correção de schema. CheckId={CheckId} CorrelationId={CorrelationId}", checkId, correlationId);
            await TryAuditAsync("SCHEMA_FIX_APPLY", userId, checkId, null, null, false, correlationId, ex.MessageText, ct);
            return Fail(result, $"Erro PostgreSQL ao aplicar correção: {ex.MessageText}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao aplicar correção de schema. CheckId={CheckId} CorrelationId={CorrelationId}", checkId, correlationId);
            await TryAuditAsync("SCHEMA_FIX_APPLY", userId, checkId, null, null, false, correlationId, ex.Message, ct);
            return Fail(result, "Erro ao aplicar correção de schema.");
        }
    }

    public async Task<SchemaRepairResultDto> ApplySafeFixesAsync(string confirmation, Guid userId, CancellationToken ct)
    {
        var correlationId = NewCorrelationId();
        var result = new SchemaRepairResultDto { CorrelationId = correlationId };

        try
        {
            var validation = ValidateApplyRequest(confirmation);
            if (validation is not null)
                return Fail(result, validation);

            var report = await _schemaHealth.CheckAsync(ct);
            var fixes = (await _fixSqlProvider.GetFixesAsync(report, ct))
                .Where(f => f.CanAutoFix && ValidateWhitelistedSql(f) is null)
                .ToList();

            if (fixes.Count == 0)
                return Fail(result, "Nenhuma correção segura pendente foi encontrada.");

            var items = await ExecuteFixesAsync(fixes, "SCHEMA_FIX_APPLY_ALL", userId, correlationId, ct);
            PopulateResultCounts(result, items);
            result.Success = result.FailedCount == 0;
            result.Message = BuildRepairMessage(result, singleFix: false);
            result.Report = await _schemaHealth.CheckAsync(ct);
            return result;
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "Erro PostgreSQL ao aplicar correções seguras de schema. CorrelationId={CorrelationId}", correlationId);
            await TryAuditAsync("SCHEMA_FIX_FAILED", userId, null, null, null, false, correlationId, ex.MessageText, ct);
            return Fail(result, $"Erro PostgreSQL ao aplicar correções: {ex.MessageText}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao aplicar correções seguras de schema. CorrelationId={CorrelationId}", correlationId);
            await TryAuditAsync("SCHEMA_FIX_FAILED", userId, null, null, null, false, correlationId, ex.Message, ct);
            return Fail(result, "Erro ao aplicar correções seguras de schema.");
        }
    }

    public async Task<string> GenerateFixScriptAsync(CancellationToken ct)
    {
        var correlationId = NewCorrelationId();
        var report = await _schemaHealth.CheckAsync(ct);
        var fixes = await _fixSqlProvider.GetFixesAsync(report, ct);
        var sb = new StringBuilder();

        sb.AppendLine("-- InovaGED - Script dinâmico de correção de schema");
        sb.AppendLine($"-- Gerado em UTC: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}Z");
        sb.AppendLine($"-- Ambiente: {_environment.EnvironmentName}");
        sb.AppendLine($"-- Usuário: {_currentUser.Email}");
        sb.AppendLine($"-- CorrelationId: {correlationId}");
        sb.AppendLine("-- Contém somente SQL idempotente previamente mapeado pelo sistema.");
        sb.AppendLine("set search_path to ged, public;");
        sb.AppendLine();

        foreach (var fix in fixes)
        {
            sb.AppendLine($"-- CheckId: {fix.CheckId}");
            sb.AppendLine($"-- Objeto: {fix.ObjectName}");
            sb.AppendLine($"-- Descrição: {fix.Description}");
            sb.AppendLine(fix.FixSql.Trim());
            sb.AppendLine();
        }

        var sql = sb.ToString();
        await TryAuditAsync("SCHEMA_FIX_SCRIPT_GENERATE", _currentUser.UserId, null, "schema_fix_script", ComputeSha256(sql), true, correlationId, null, ct);
        return sql;
    }

    public Task<SchemaFixPreflightResult> ValidateFixAsync(SchemaFixDto fix, CancellationToken ct)
        => ValidateFixCoreAsync(fix, _currentUser.UserId, NewCorrelationId(), audit: true, ct);

    private string? ValidateApplyRequest(string confirmation)
    {
        var options = _options.Value;
        if (!options.Enabled)
            return "Aplicação automática de correções de schema está desabilitada.";

        if (_environment.IsProduction() && !options.AllowApplyInProduction)
            return "Aplicação automática desabilitada em produção. Baixe o script e execute manualmente com o DBA.";

        if (options.RequireConfirmationText && !string.Equals(confirmation?.Trim(), options.ConfirmationText, StringComparison.Ordinal))
            return $"Confirmação inválida. Digite exatamente: {options.ConfirmationText}";

        return null;
    }

    private async Task<List<SchemaRepairItemResultDto>> ExecuteFixesAsync(IReadOnlyList<SchemaFixDto> fixes, string action, Guid userId, string correlationId, CancellationToken ct)
    {
        var results = new List<SchemaRepairItemResultDto>();
        var transactionalFixes = fixes.Where(f => !IsIndexFix(f)).ToList();
        var indexFixes = fixes.Where(IsIndexFix).ToList();

        await using var conn = await _db.OpenAsync(ct);

        var preflightByCheckId = new Dictionary<string, SchemaFixPreflightResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var fix in fixes)
        {
            var preflight = await ValidateFixCoreAsync(fix, userId, correlationId, audit: true, ct);
            preflightByCheckId[fix.CheckId] = preflight;

            if (preflight.AlreadyApplied || preflight.ShouldSkip)
            {
                results.Add(new SchemaRepairItemResultDto
                {
                    CheckId = fix.CheckId,
                    Success = preflight.AlreadyApplied,
                    Skipped = true,
                    Message = preflight.Message
                });
                await TryAuditAsync(preflight.AlreadyApplied ? "SCHEMA_FIX_SKIP" : "SCHEMA_FIX_SKIP", userId, fix.CheckId, fix.ObjectName, ComputeSha256(fix.FixSql), true, correlationId, preflight.Message, ct);
            }
            else if (!preflight.CanRun)
            {
                var isOptional = IsIndexFix(fix) || string.Equals(fix.RiskLevel, "Low", StringComparison.OrdinalIgnoreCase);
                results.Add(new SchemaRepairItemResultDto
                {
                    CheckId = fix.CheckId,
                    Success = false,
                    Skipped = isOptional,
                    Message = isOptional ? "Correção não aplicável ao schema atual." : "Correção bloqueada pelo preflight.",
                    Error = preflight.Message
                });
                await TryAuditAsync(isOptional ? "SCHEMA_FIX_SKIP" : "SCHEMA_FIX_FAILED", userId, fix.CheckId, fix.ObjectName, ComputeSha256(fix.FixSql), false, correlationId, preflight.Message, ct);
            }
        }

        transactionalFixes = transactionalFixes.Where(f => preflightByCheckId.TryGetValue(f.CheckId, out var p) && p.CanRun && !p.AlreadyApplied && !p.ShouldSkip).ToList();
        indexFixes = indexFixes.Where(f => preflightByCheckId.TryGetValue(f.CheckId, out var p) && p.CanRun && !p.AlreadyApplied && !p.ShouldSkip).ToList();

        if (transactionalFixes.Count > 0)
        {
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                foreach (var fix in transactionalFixes)
                {
                    _logger.LogInformation("Aplicando correção crítica de schema {CheckId}. CorrelationId={CorrelationId} SqlHash={SqlHash}", fix.CheckId, correlationId, ComputeSha256(fix.FixSql));
                    var sql = preflightByCheckId[fix.CheckId].SafeSqlToRun ?? fix.FixSql;
                    await conn.ExecuteAsync(new CommandDefinition(sql, transaction: tx, commandTimeout: CommandTimeoutSeconds, cancellationToken: ct));
                }

                await tx.CommitAsync(ct);

                foreach (var fix in transactionalFixes)
                {
                    results.Add(new SchemaRepairItemResultDto
                    {
                        CheckId = fix.CheckId,
                        Success = true,
                        Message = "Correção crítica aplicada em transação após preflight."
                    });
                    await TryAuditAsync("SCHEMA_FIX_APPLY", userId, fix.CheckId, fix.ObjectName, ComputeSha256(preflightByCheckId[fix.CheckId].SafeSqlToRun ?? fix.FixSql), true, correlationId, null, ct);
                }
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }

        foreach (var fix in indexFixes)
        {
            try
            {
                _logger.LogInformation("Aplicando índice recomendado de schema {CheckId}. CorrelationId={CorrelationId} SqlHash={SqlHash}", fix.CheckId, correlationId, ComputeSha256(fix.FixSql));
                var sql = preflightByCheckId[fix.CheckId].SafeSqlToRun ?? fix.FixSql;
                await conn.ExecuteAsync(new CommandDefinition(sql, commandTimeout: CommandTimeoutSeconds, cancellationToken: ct));

                results.Add(new SchemaRepairItemResultDto
                {
                    CheckId = fix.CheckId,
                    Success = true,
                    Message = "Índice recomendado aplicado ou já existente."
                });
                await TryAuditAsync("SCHEMA_FIX_APPLY", userId, fix.CheckId, fix.ObjectName, ComputeSha256(sql), true, correlationId, null, ct);
            }
            catch (PostgresException ex) when (ex.SqlState == "42703")
            {
                _logger.LogWarning(ex,
                    "Índice recomendado não aplicado por coluna ausente. CheckId={CheckId} Object={ObjectName} CorrelationId={CorrelationId}",
                    fix.CheckId,
                    fix.ObjectName,
                    correlationId);

                results.Add(new SchemaRepairItemResultDto
                {
                    CheckId = fix.CheckId,
                    Success = false,
                    Skipped = true,
                    Message = "Índice não aplicado porque a coluna esperada não existe no schema atual.",
                    Error = ex.MessageText
                });
                await TryAuditAsync("SCHEMA_FIX_SKIP", userId, fix.CheckId, fix.ObjectName, ComputeSha256(fix.FixSql), false, correlationId, ex.MessageText, ct);
            }
            catch (PostgresException ex)
            {
                _logger.LogWarning(ex,
                    "Índice recomendado falhou e as demais correções continuarão. CheckId={CheckId} Object={ObjectName} CorrelationId={CorrelationId}",
                    fix.CheckId,
                    fix.ObjectName,
                    correlationId);

                results.Add(new SchemaRepairItemResultDto
                {
                    CheckId = fix.CheckId,
                    Success = false,
                    Message = "Índice recomendado não aplicado; as demais correções continuaram.",
                    Error = ex.MessageText
                });
                await TryAuditAsync("SCHEMA_FIX_FAILED", userId, fix.CheckId, fix.ObjectName, ComputeSha256(fix.FixSql), false, correlationId, ex.MessageText, ct);
            }
        }

        return results;
    }

    private async Task<SchemaFixPreflightResult> ValidateFixCoreAsync(SchemaFixDto fix, Guid userId, string correlationId, bool audit, CancellationToken ct)
    {
        var result = new SchemaFixPreflightResult();
        try
        {
            if (!fix.CanAutoFix)
                return PreflightBlocked(result, "Correção não marcada como automática/segura.");

            var requestValidation = ValidateExecutionEnvironment();
            if (requestValidation is not null)
                return PreflightBlocked(result, requestValidation);

            var safety = ValidateWhitelistedSql(fix);
            if (safety is not null)
                return PreflightBlocked(result, safety);

            await using var conn = await _db.OpenAsync(ct);

            if (IsDocumentSearchTenantVersionFix(fix))
            {
                await ValidateDocumentSearchTenantVersionIndexAsync(conn, fix, result, ct);
            }
            else
            {
                await ValidateDeclaredDependenciesAsync(conn, fix, result, ct);
                result.AlreadyApplied = await IsFixAlreadyAppliedAsync(conn, fix, ct);
                if (result.AlreadyApplied)
                {
                    result.CanRun = false;
                    result.ShouldSkip = true;
                    result.Message = "Correção já aplicada no schema atual.";
                }
                else if (result.MissingDependencies.Count == 0)
                {
                    result.CanRun = true;
                    result.SafeSqlToRun = fix.FixSql;
                    result.Message = "Preflight aprovado: dependências encontradas e SQL seguro.";
                }
                else
                {
                    result.CanRun = false;
                    result.ShouldSkip = IsIndexFix(fix);
                    result.Message = "Dependências ausentes: " + string.Join(", ", result.MissingDependencies);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Preflight de correção de schema falhou. CheckId={CheckId} CorrelationId={CorrelationId}", fix.CheckId, correlationId);
            result.CanRun = false;
            result.Message = "Falha ao validar preflight da correção.";
        }

        if (audit)
        {
            await TryAuditAsync("SCHEMA_FIX_PREFLIGHT", userId, fix.CheckId, fix.ObjectName, ComputeSha256(fix.FixSql), result.CanRun || result.AlreadyApplied || result.ShouldSkip, correlationId, result.Message, ct);
        }

        return result;
    }

    private string? ValidateExecutionEnvironment()
    {
        var options = _options.Value;
        if (!options.Enabled)
            return "Aplicação automática de correções de schema está desabilitada.";

        if (_environment.IsProduction() && !options.AllowApplyInProduction)
            return "Aplicação automática desabilitada em produção.";

        return null;
    }

    private static SchemaFixPreflightResult PreflightBlocked(SchemaFixPreflightResult result, string message)
    {
        result.CanRun = false;
        result.Message = message;
        return result;
    }

    private static bool IsDocumentSearchTenantVersionFix(SchemaFixDto fix)
        => string.Equals(fix.CheckId, "GED_INDEX_DOCUMENT_SEARCH_TENANT_VERSION", StringComparison.OrdinalIgnoreCase);

    private async Task ValidateDocumentSearchTenantVersionIndexAsync(NpgsqlConnection conn, SchemaFixDto fix, SchemaFixPreflightResult result, CancellationToken ct)
    {
        var tableExists = await ObjectExistsAsync(conn, "Table", "ged", "document_search", null, ct);
        if (!tableExists)
        {
            result.ShouldSkip = true;
            result.Message = "Tabela ged.document_search não existe. Índice não aplicável ao schema atual.";
            result.MissingDependencies.Add("table ged.document_search");
            return;
        }

        var hasTenantId = await ObjectExistsAsync(conn, "Column", "ged", "document_search", "tenant_id", ct);
        if (!hasTenantId)
        {
            result.ShouldSkip = true;
            result.Message = "Coluna ged.document_search.tenant_id não existe. Índice não aplicável ao schema atual.";
            result.MissingDependencies.Add("column ged.document_search.tenant_id");
            return;
        }

        var candidates = new[]
        {
            (Column: "document_version_id", Index: "ix_ged_document_search_tenant_document_version"),
            (Column: "version_id", Index: "ix_ged_document_search_tenant_version"),
            (Column: "document_id", Index: "ix_ged_document_search_tenant_document")
        };

        foreach (var candidate in candidates)
        {
            if (await IndexExistsAsync(conn, "ged", candidate.Index, ct))
            {
                result.AlreadyApplied = true;
                result.ShouldSkip = true;
                result.Message = $"Índice recomendado já existe ({candidate.Index}).";
                return;
            }
        }

        foreach (var candidate in candidates)
        {
            if (await ObjectExistsAsync(conn, "Column", "ged", "document_search", candidate.Column, ct))
            {
                result.CanRun = true;
                result.SafeSqlToRun = fix.FixSql;
                result.Message = $"Preflight aprovado: o índice document_search será criado usando tenant_id + {candidate.Column}.";
                return;
            }
        }

        result.ShouldSkip = true;
        result.Message = "Nenhuma coluna de vínculo encontrada em ged.document_search. Índice não aplicável ao schema atual.";
        result.MissingDependencies.Add("column ged.document_search.document_version_id|version_id|document_id");
    }

    private async Task ValidateDeclaredDependenciesAsync(NpgsqlConnection conn, SchemaFixDto fix, SchemaFixPreflightResult result, CancellationToken ct)
    {
        foreach (var dependency in fix.Dependencies.Where(d => d.Required))
        {
            if (!await ObjectExistsAsync(conn, dependency.Type, dependency.Schema, dependency.Table, dependency.Column, ct))
                result.MissingDependencies.Add(FormatDependency(dependency));
        }
    }

    private static string FormatDependency(SchemaObjectDependency dependency)
    {
        return dependency.Type.ToLowerInvariant() switch
        {
            "schema" => $"schema {dependency.Schema}",
            "table" => $"table {dependency.Schema}.{dependency.Table}",
            "column" => $"column {dependency.Schema}.{dependency.Table}.{dependency.Column}",
            "extension" => $"extension {dependency.Schema}",
            _ => dependency.Type
        };
    }

    private async Task<bool> IsFixAlreadyAppliedAsync(NpgsqlConnection conn, SchemaFixDto fix, CancellationToken ct)
    {
        if (string.Equals(fix.FixType, "Index", StringComparison.OrdinalIgnoreCase) || IsIndexFix(fix))
            return await IndexExistsAsync(conn, "ged", GetObjectLeafName(fix.ObjectName), ct);

        if (string.Equals(fix.FixType, "Table", StringComparison.OrdinalIgnoreCase))
            return await ObjectExistsAsync(conn, "Table", "ged", GetObjectLeafName(fix.ObjectName), null, ct);

        if (string.Equals(fix.FixType, "Column", StringComparison.OrdinalIgnoreCase))
        {
            var parts = fix.ObjectName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 3)
                return await ObjectExistsAsync(conn, "Column", parts[^3], parts[^2], parts[^1], ct);
        }

        return false;
    }

    private static string GetObjectLeafName(string objectName)
    {
        var parts = objectName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? objectName : parts[^1];
    }

    private static Task<bool> ObjectExistsAsync(NpgsqlConnection conn, string type, string schema, string table, string? column, CancellationToken ct)
    {
        var normalizedType = (type ?? string.Empty).Trim().ToLowerInvariant();
        return normalizedType switch
        {
            "schema" => conn.ExecuteScalarAsync<bool>(new CommandDefinition("select exists (select 1 from information_schema.schemata where schema_name = @schema);", new { schema }, cancellationToken: ct)),
            "table" => conn.ExecuteScalarAsync<bool>(new CommandDefinition("select exists (select 1 from information_schema.tables where table_schema = @schema and table_name = @table);", new { schema, table }, cancellationToken: ct)),
            "column" => conn.ExecuteScalarAsync<bool>(new CommandDefinition("select exists (select 1 from information_schema.columns where table_schema = @schema and table_name = @table and column_name = @column);", new { schema, table, column }, cancellationToken: ct)),
            "extension" => conn.ExecuteScalarAsync<bool>(new CommandDefinition("select exists (select 1 from pg_extension where extname = @extensionName);", new { extensionName = schema }, cancellationToken: ct)),
            _ => Task.FromResult(true)
        };
    }

    private static Task<bool> IndexExistsAsync(NpgsqlConnection conn, string schema, string indexName, CancellationToken ct)
        => conn.ExecuteScalarAsync<bool>(new CommandDefinition("select exists (select 1 from pg_indexes where schemaname = @schema and indexname = @indexName);", new { schema, indexName }, cancellationToken: ct));

    private static void PopulateResultCounts(SchemaRepairResultDto result, List<SchemaRepairItemResultDto> items)
    {
        result.Items = items;
        result.AppliedCount = items.Count(i => i.Success && !i.Skipped);
        result.FailedCount = items.Count(i => !i.Success && !i.Skipped);
        result.SkippedCount = items.Count(i => i.Skipped);
    }

    private static string BuildRepairMessage(SchemaRepairResultDto result, bool singleFix)
    {
        if (singleFix)
        {
            if (result.AppliedCount == 1)
                return "Correção aplicada com sucesso.";
            if (result.SkippedCount == 1)
                return "Correção ignorada com segurança.";
            return "Falha ao aplicar correção.";
        }

        if (result.FailedCount == 0 && result.SkippedCount > 0)
            return "Correções aplicadas. Algumas recomendações de performance foram ignoradas por incompatibilidade com o schema atual.";

        return $"Correções processadas. Aplicadas: {result.AppliedCount}; ignoradas: {result.SkippedCount}; falharam: {result.FailedCount}.";
    }

    private static bool IsIndexFix(SchemaFixDto fix)
        => fix.CheckId.Contains("_INDEX_", StringComparison.OrdinalIgnoreCase)
           || fix.ObjectName.Contains(".ix_", StringComparison.OrdinalIgnoreCase);

    private static string? ValidateWhitelistedSql(SchemaFixDto fix)
    {
        if (string.IsNullOrWhiteSpace(fix.CheckId) || string.IsNullOrWhiteSpace(fix.FixSql))
            return "Correção inválida ou sem SQL.";

        var sql = fix.FixSql.ToLowerInvariant();
        var forbidden = new[] { "drop ", "truncate ", "delete ", "update " };
        if (forbidden.Any(sql.Contains))
            return "SQL bloqueado pela política de segurança (operação destrutiva não permitida).";

        if (!sql.Contains("if not exists") && !sql.Contains("do $$"))
            return "SQL bloqueado: correções automáticas devem ser idempotentes.";

        return null;
    }

    private async Task TryAuditAsync(string action, Guid userId, string? checkId, string? objectName, string? sqlHash, bool success, string correlationId, string? error, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var hasAppAuditLog = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(@"
select exists (
    select 1
    from information_schema.tables
    where table_schema = 'ged' and table_name = 'app_audit_log'
);", cancellationToken: ct));

            if (!hasAppAuditLog)
                return;

            await conn.ExecuteAsync(new CommandDefinition(@"
insert into ged.app_audit_log
(tenant_id, user_id, user_name, action, event_type, source, entity_name, entity_id, message, details, correlation_id, created_at)
values
(@TenantId, @UserId, @UserName, @Action, @EventType, 'SYSTEM_HEALTH', 'SchemaRepair', @EntityId, @Message, @Details::jsonb, @CorrelationId, now());",
                new
                {
                    TenantId = _currentUser.TenantId == Guid.Empty ? (Guid?)null : _currentUser.TenantId,
                    UserId = userId == Guid.Empty ? (Guid?)null : userId,
                    UserName = _currentUser.Email,
                    Action = action,
                    EventType = success ? "INFO" : "ERROR",
                    EntityId = checkId,
                    Message = success ? "Correção de schema executada." : "Falha na correção de schema.",
                    Details = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        checkId,
                        objectName,
                        sqlHash,
                        success,
                        environment = _environment.EnvironmentName,
                        error
                    }),
                    CorrelationId = correlationId
                }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível registrar auditoria de correção de schema. Action={Action} CheckId={CheckId} CorrelationId={CorrelationId}", action, checkId, correlationId);
        }
    }

    private static SchemaRepairResultDto Fail(SchemaRepairResultDto result, string message)
    {
        result.Success = false;
        result.Message = message;
        return result;
    }

    private static string NewCorrelationId() => $"schema-repair-{Guid.NewGuid():N}";

    private static string ComputeSha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
