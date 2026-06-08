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

            var success = await ExecuteFixesAsync([fix], "SCHEMA_FIX_APPLY", userId, correlationId, ct);
            result.Success = success;
            result.AppliedCount = success ? 1 : 0;
            result.Message = success ? "Correção aplicada com sucesso." : "Falha ao aplicar correção.";
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

            var success = await ExecuteFixesAsync(fixes, "SCHEMA_FIX_APPLY_ALL", userId, correlationId, ct);
            result.Success = success;
            result.AppliedCount = success ? fixes.Count : 0;
            result.Message = success ? $"{fixes.Count} correções seguras aplicadas com sucesso." : "Falha ao aplicar correções seguras.";
            result.Report = await _schemaHealth.CheckAsync(ct);
            return result;
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "Erro PostgreSQL ao aplicar correções seguras de schema. CorrelationId={CorrelationId}", correlationId);
            await TryAuditAsync("SCHEMA_FIX_APPLY_ALL", userId, null, null, null, false, correlationId, ex.MessageText, ct);
            return Fail(result, $"Erro PostgreSQL ao aplicar correções: {ex.MessageText}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao aplicar correções seguras de schema. CorrelationId={CorrelationId}", correlationId);
            await TryAuditAsync("SCHEMA_FIX_APPLY_ALL", userId, null, null, null, false, correlationId, ex.Message, ct);
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

    private async Task<bool> ExecuteFixesAsync(IReadOnlyList<SchemaFixDto> fixes, string action, Guid userId, string correlationId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            foreach (var fix in fixes)
            {
                _logger.LogInformation("Aplicando correção de schema {CheckId}. CorrelationId={CorrelationId} SqlHash={SqlHash}", fix.CheckId, correlationId, ComputeSha256(fix.FixSql));
                await conn.ExecuteAsync(new CommandDefinition(fix.FixSql, transaction: tx, commandTimeout: CommandTimeoutSeconds, cancellationToken: ct));
            }

            await tx.CommitAsync(ct);

            foreach (var fix in fixes)
            {
                await TryAuditAsync(action, userId, fix.CheckId, fix.ObjectName, ComputeSha256(fix.FixSql), true, correlationId, null, ct);
            }

            return true;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

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
