using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

return await MigratorProgram.RunAsync(args);

internal static class MigratorProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddSimpleConsole(options => options.SingleLine = true));
        var logger = loggerFactory.CreateLogger("InovaGed.Database.Migrator");
        var command = args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal)) ?? "status";
        var verify = command == "verify" || args.Contains("--verify", StringComparer.OrdinalIgnoreCase);
        if (command is not ("status" or "apply" or "verify")) { logger.LogError("Comando inválido. Use status, apply ou verify."); return 2; }

        var configuration = new ConfigurationBuilder().AddJsonFile("appsettings.json", true).AddUserSecrets(typeof(MigratorProgram).Assembly, true).AddEnvironmentVariables().Build();
        var connectionString = configuration.GetConnectionString("DefaultConnection") ?? configuration["DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(connectionString)) { logger.LogError("Defina ConnectionStrings__DefaultConnection ou DATABASE_URL."); return 2; }

        try
        {
            var root = FindRepositoryRoot();
            var manifest = JsonSerializer.Deserialize<MigrationManifest>(await File.ReadAllTextAsync(Path.Combine(root, "database/migrations.manifest.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("Manifesto de migrations inválido.");
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await EnsureHistoryAsync(connection);
            if (command == "apply") await ApplyAsync(connection, root, manifest, logger);
            if (command == "status") await StatusAsync(connection, root, manifest, logger);
            if (verify) await VerifyAsync(connection);
            return 0;
        }
        catch (Exception exception)
        {
            logger.LogError("Operação de migration falhou: {ErrorCode}", exception.GetType().Name);
            return 1;
        }
    }

    private static async Task ApplyAsync(NpgsqlConnection connection, string root, MigrationManifest manifest, ILogger logger)
    {
        await new NpgsqlCommand("SELECT pg_advisory_lock(hashtext('inovaged:database:migrations'))", connection).ExecuteNonQueryAsync();
        try
        {
            foreach (var migration in manifest.Migrations)
            {
                var path = Path.GetFullPath(Path.Combine(root, migration.Path));
                if (!path.StartsWith(root, StringComparison.Ordinal)) throw new InvalidOperationException("Caminho de migration fora do repositório.");
                var sql = await File.ReadAllTextAsync(path);
                if (sql.Contains("\\ir", StringComparison.Ordinal)) throw new InvalidOperationException("O manifesto não aceita metacomandos psql.");
                var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql))).ToLowerInvariant();
                await using var check = new NpgsqlCommand("SELECT checksum_sha256, status FROM ged.schema_migration_history WHERE script_name = $1 ORDER BY applied_at_utc DESC LIMIT 1", connection);
                check.Parameters.AddWithValue(migration.Id);
                await using var reader = await check.ExecuteReaderAsync();
                string? existing = null; string? status = null;
                if (await reader.ReadAsync()) { existing = reader.GetString(0); status = reader.GetString(1); }
                await reader.DisposeAsync();
                if (status == "APPLIED" && existing == checksum) { logger.LogInformation("{Migration}: já aplicada.", migration.Id); continue; }
                if (existing is not null) throw new InvalidOperationException($"Checksum divergente ou tentativa anterior pendente: {migration.Id}");

                var stopwatch = Stopwatch.StartNew();
                await using var transaction = migration.Transactional ? await connection.BeginTransactionAsync() : null;
                try
                {
                    await new NpgsqlCommand(sql, connection, transaction).ExecuteNonQueryAsync();
                    await RecordAsync(connection, transaction, migration.Id, checksum, stopwatch.ElapsedMilliseconds, "APPLIED", null);
                    if (transaction is not null) await transaction.CommitAsync();
                    logger.LogInformation("{Migration}: aplicada em {Elapsed} ms.", migration.Id, stopwatch.ElapsedMilliseconds);
                }
                catch (Exception exception)
                {
                    if (transaction is not null) await transaction.RollbackAsync();
                    await RecordAsync(connection, null, migration.Id, checksum, stopwatch.ElapsedMilliseconds, "FAILED", exception.GetType().Name);
                    throw;
                }
            }
        }
        finally { await new NpgsqlCommand("SELECT pg_advisory_unlock(hashtext('inovaged:database:migrations'))", connection).ExecuteNonQueryAsync(); }
    }

    private static async Task EnsureHistoryAsync(NpgsqlConnection connection) => await new NpgsqlCommand("""
        CREATE SCHEMA IF NOT EXISTS ged;
        CREATE TABLE IF NOT EXISTS ged.schema_migration_history (
          id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, script_name text NOT NULL,
          checksum_sha256 text NOT NULL, applied_at_utc timestamptz NOT NULL DEFAULT now(),
          execution_ms bigint NOT NULL DEFAULT 0, status text NOT NULL,
          application_version text NOT NULL DEFAULT '04.1.20', error_code text NULL, notes text NULL);
        ALTER TABLE ged.schema_migration_history ADD COLUMN IF NOT EXISTS checksum_sha256 text;
        ALTER TABLE ged.schema_migration_history ADD COLUMN IF NOT EXISTS applied_at_utc timestamptz NOT NULL DEFAULT now();
        ALTER TABLE ged.schema_migration_history ADD COLUMN IF NOT EXISTS execution_ms bigint NOT NULL DEFAULT 0;
        ALTER TABLE ged.schema_migration_history ADD COLUMN IF NOT EXISTS status text NOT NULL DEFAULT 'APPLIED';
        ALTER TABLE ged.schema_migration_history ADD COLUMN IF NOT EXISTS application_version text NOT NULL DEFAULT 'legacy';
        ALTER TABLE ged.schema_migration_history ADD COLUMN IF NOT EXISTS error_code text NULL;
        ALTER TABLE ged.schema_migration_history ADD COLUMN IF NOT EXISTS notes text NULL;
        CREATE UNIQUE INDEX IF NOT EXISTS ux_schema_migration_history_applied ON ged.schema_migration_history(script_name) WHERE status = 'APPLIED';
        """, connection).ExecuteNonQueryAsync();

    private static async Task RecordAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string id, string checksum, long elapsed, string status, string? error) { await using var cmd = new NpgsqlCommand("INSERT INTO ged.schema_migration_history(script_name,checksum_sha256,execution_ms,status,application_version,error_code) VALUES ($1,$2,$3,$4,'04.1.20',$5)", connection, transaction); cmd.Parameters.AddWithValue(id); cmd.Parameters.AddWithValue(checksum); cmd.Parameters.AddWithValue(elapsed); cmd.Parameters.AddWithValue(status); cmd.Parameters.AddWithValue((object?)error ?? DBNull.Value); await cmd.ExecuteNonQueryAsync(); }
    private static async Task StatusAsync(NpgsqlConnection connection, string root, MigrationManifest manifest, ILogger logger) { foreach (var item in manifest.Migrations) logger.LogInformation("{Migration}: {State}", item.Id, await IsAppliedAsync(connection, item.Id) ? "APPLIED" : "PENDING"); }
    private static async Task<bool> IsAppliedAsync(NpgsqlConnection connection, string id) { await using var cmd = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM ged.schema_migration_history WHERE script_name=$1 AND status='APPLIED')", connection); cmd.Parameters.AddWithValue(id); return (bool)(await cmd.ExecuteScalarAsync() ?? false); }
    private static async Task VerifyAsync(NpgsqlConnection connection) { var required = new[] { "backup_policy", "backup_job", "backup_set", "backup_artifact", "backup_verification", "restore_test", "recovery_plan", "recovery_plan_version", "recovery_test", "recovery_objective_measurement", "portability_export", "portability_export_item", "portability_artifact", "tenant_offboarding", "tenant_offboarding_event", "data_retention_hold", "operations_worker_heartbeat", "operations_dead_letter", "operation_job_event" }; foreach (var table in required) { await using var cmd = new NpgsqlCommand("SELECT to_regclass('ged.' || $1) IS NOT NULL", connection); cmd.Parameters.AddWithValue(table); if (!(bool)(await cmd.ExecuteScalarAsync() ?? false)) throw new InvalidOperationException($"Objeto obrigatório ausente: ged.{table}"); } }
    private static string FindRepositoryRoot() { var current = new DirectoryInfo(Directory.GetCurrentDirectory()); while (current is not null && !File.Exists(Path.Combine(current.FullName, "InovaGed.sln"))) current = current.Parent; return current?.FullName ?? throw new DirectoryNotFoundException("Raiz do repositório não encontrada."); }
}

internal sealed record MigrationManifest(int Version, IReadOnlyList<MigrationEntry> Migrations);
internal sealed record MigrationEntry(string Id, string Path, string Module, bool Transactional);
