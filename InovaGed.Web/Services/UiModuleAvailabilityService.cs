using Npgsql;

namespace InovaGed.Web.Services;

public sealed record UiModuleAvailability(bool Enabled, bool SchemaReady, bool Available, string Status, string? UserMessage, string? AdministratorMessage);

public interface IUiModuleAvailabilityService
{
    Task<UiModuleAvailability> GetContinuityAsync(CancellationToken cancellationToken = default);
}

public sealed class UiModuleAvailabilityService(IConfiguration configuration, ILogger<UiModuleAvailabilityService> logger) : IUiModuleAvailabilityService
{
    public async Task<UiModuleAvailability> GetContinuityAsync(CancellationToken cancellationToken = default)
    {
        var enabled = configuration.GetValue<bool>("Backup:Enabled") || configuration.GetValue<bool>("Portability:Enabled");
        if (!enabled)
            return new(false, false, false, "NOT_CONFIGURED", "Módulo de continuidade não configurado", "Habilite Backup:Enabled ou Portability:Enabled após configurar o ambiente.");

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            return new(true, false, false, "SCHEMA_PENDING", "Módulo indisponível", "Configure a conexão e aplique database/migrations/2026_07_backup_continuity_portability.sql.");

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT to_regclass('ged.backup_set') IS NOT NULL AND to_regclass('ged.backup_policy') IS NOT NULL AND to_regclass('ged.backup_job') IS NOT NULL AND to_regclass('ged.backup_verification') IS NOT NULL AND to_regclass('ged.recovery_plan') IS NOT NULL AND to_regclass('ged.portability_export') IS NOT NULL", connection);
            var ready = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
            return ready
                ? new(true, true, true, "AVAILABLE", null, null)
                : new(true, false, false, "SCHEMA_PENDING", "Módulo indisponível", "As migrations de continuidade ainda não foram aplicadas.");
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException)
        {
            logger.LogWarning(exception, "Continuity schema availability could not be confirmed");
            return new(true, false, false, "SCHEMA_UNAVAILABLE", "Módulo temporariamente indisponível", "Verifique a conexão e execute database/assert_continuity_schema.sql.");
        }
    }
}
