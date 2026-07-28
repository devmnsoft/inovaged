using InovaGed.Application.Readiness;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
namespace InovaGed.Infrastructure.Readiness;
public sealed class PostgresModuleReadinessService(IConfiguration configuration, ILogger<PostgresModuleReadinessService> logger) : IModuleReadinessService
{
    private static readonly string[] Objects = ["backup_policy", "backup_job", "backup_set", "backup_artifact", "backup_verification", "restore_test", "recovery_plan", "recovery_plan_version", "recovery_test", "recovery_objective_measurement", "portability_export", "portability_export_item", "portability_artifact", "tenant_offboarding", "tenant_offboarding_event", "data_retention_hold", "operations_worker_heartbeat", "operations_dead_letter", "operation_job_event"];
    public async Task<ModuleReadinessResult> GetAsync(string code, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow; var continuity = code.Equals("Continuity", StringComparison.OrdinalIgnoreCase);
        var enabled = !continuity || configuration.GetValue<bool>("Backup:Enabled") || configuration.GetValue<bool>("Portability:Enabled");
        if (!enabled) return Result(code, false, false, true, "NOT_CONFIGURED", Objects, ["Habilite o módulo depois de configurar suas dependências."], now);
        var cs = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs)) return Result(code, true, false, false, "DEPENDENCY_PENDING", [], ["Configure a conexão de banco sem expor credenciais."], now);
        try
        {
            await using var connection = new NpgsqlConnection(cs); await connection.OpenAsync(ct);
            if (!continuity) return Result(code, true, true, true, "AVAILABLE", [], [], now);
            var missing = new List<string>();
            foreach (var name in Objects) { await using var command = new NpgsqlCommand("SELECT to_regclass('ged.' || $1) IS NOT NULL", connection); command.Parameters.AddWithValue(name); if (!(bool)(await command.ExecuteScalarAsync(ct) ?? false)) missing.Add($"ged.{name}"); }
            return missing.Count == 0 ? Result(code, true, true, true, "AVAILABLE", [], [], now) : Result(code, true, false, true, "SCHEMA_PENDING", missing, ["Execute o Migrator oficial com apply --verify."], now);
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException) { logger.LogWarning("Readiness failed for {Module}: {Code}", code, exception.GetType().Name); return Result(code, true, false, false, "UNAVAILABLE", [], ["Verifique a conectividade e revalide o ambiente."], now); }
    }
    private static ModuleReadinessResult Result(string code, bool enabled, bool schema, bool dependencies, string status, IReadOnlyList<string> missing, IReadOnlyList<string> recommendations, DateTimeOffset now) => new(code, enabled, schema, dependencies, enabled && schema && dependencies, status, missing, recommendations, now);
}
