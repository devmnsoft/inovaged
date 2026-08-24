using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.SystemHealth.Migrations;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace InovaGed.Infrastructure.SystemHealth.Migrations;

public sealed class DatabaseMigrationRunner(IDbConnectionFactory db, IHostEnvironment environment) : IDatabaseMigrationRunner
{
    private static readonly Regex DestructiveSql = new(@"\b(drop\s+(table|schema)|truncate|delete\s+from)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly IReadOnlySet<string> DestructiveAllowList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly string _root = FindRoot(environment.ContentRootPath);

    public async Task<DatabaseMigrationPlan> GetPlanAsync(CancellationToken ct)
    {
        var catalog = await ReadCatalogAsync(ct);
        await using var connection = await db.OpenAsync(ct);
        await EnsureHistoryAsync(connection, ct);
        var history = (await connection.QueryAsync<HistoryRow>(new CommandDefinition("""
            select script_name as ScriptName, checksum_sha256 as ChecksumSha256, success as Success,
                   applied_at as AppliedAt, error_message as ErrorMessage
              from ged.schema_migration_history where reg_status = 'A' order by applied_at desc
            """, cancellationToken: ct))).ToList();
        var items = new List<DatabaseMigrationPlanItem>();
        foreach (var entry in catalog)
        {
            var fullPath = ResolveCatalogPath(entry.Path);
            var checksum = File.Exists(fullPath) ? await ChecksumAsync(fullPath, ct) : null;
            var rows = history.Where(x => x.ScriptName.Equals(entry.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            var success = rows.FirstOrDefault(x => x.Success);
            var applied = success is not null && string.Equals(success.ChecksumSha256, checksum, StringComparison.OrdinalIgnoreCase);
            var failed = rows.FirstOrDefault(x => !x.Success);
            var lastError = !File.Exists(fullPath) ? "Arquivo de migration não encontrado." : success is not null && !applied ? "Checksum difere da versão aplicada; revisão manual obrigatória." : failed?.ErrorMessage;
            items.Add(new(entry.Name, entry.Path, entry.Area, entry.Description, entry.Required, applied, failed is not null || success is not null && !applied, checksum, success?.AppliedAt, lastError));
        }
        return new(items, items.Count, items.Count(x => x.Applied), items.Count(x => x.Required && !x.Applied), items.Count(x => x.FailedBefore));
    }

    public async Task<DatabaseMigrationResult> ApplyRequiredAsync(Guid? userId, string? userName, CancellationToken ct)
    {
        var plan = await GetPlanAsync(ct);
        var results = new List<DatabaseMigrationExecutionItem>();
        foreach (var item in plan.Items.Where(x => x.Required && !x.Applied))
        {
            var result = await ApplyOneAsync(item.Name, userId, userName, ct);
            results.AddRange(result.Items);
            if (!result.Success) break;
        }
        return new(results.All(x => x.Success), results.Count == 0 ? "Nenhuma migration pendente." : results.All(x => x.Success) ? "Migrations obrigatórias aplicadas." : "Aplicação interrompida após falha; consulte o relatório.", results);
    }

    public async Task<DatabaseMigrationResult> ApplyOneAsync(string migrationName, Guid? userId, string? userName, CancellationToken ct)
    {
        var catalog = await ReadCatalogAsync(ct);
        var entry = catalog.SingleOrDefault(x => x.Name.Equals(migrationName, StringComparison.OrdinalIgnoreCase));
        if (entry is null) return Failure(migrationName, "", "Migration não pertence ao catálogo obrigatório.");
        var planItem = (await GetPlanAsync(ct)).Items.Single(x => x.Name == entry.Name);
        if (planItem.Applied) return new(true, "Migration já aplicada; execução duplicada evitada.", []);
        if (planItem.LastError?.StartsWith("Checksum difere", StringComparison.Ordinal) == true) return Failure(entry.Name, entry.Path, planItem.LastError);
        var fullPath = ResolveCatalogPath(entry.Path);
        if (!File.Exists(fullPath)) return Failure(entry.Name, entry.Path, "Arquivo de migration não encontrado.");
        var sql = await File.ReadAllTextAsync(fullPath, ct);
        if (DestructiveSql.IsMatch(RemoveComments(sql)) && !DestructiveAllowList.Contains(entry.Name)) return Failure(entry.Name, entry.Path, "Migration bloqueada: comando destrutivo detectado.");
        var checksum = await ChecksumAsync(fullPath, ct);
        var watch = Stopwatch.StartNew();
        await using var connection = await db.OpenAsync(ct);
        await EnsureHistoryAsync(connection, ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(sql, transaction: transaction, commandTimeout: 300, cancellationToken: ct));
            watch.Stop();
            await connection.ExecuteAsync(new CommandDefinition(InsertHistorySql, new { entry.Name, Path = entry.Path, Checksum = checksum, UserId = userId, UserName = userName, Success = true, Duration = (int)watch.ElapsedMilliseconds, Error = (string?)null }, transaction, cancellationToken: ct));
            await transaction.CommitAsync(ct);
            return new(true, "Migration aplicada com sucesso.", [new(entry.Name, entry.Path, true, (int)watch.ElapsedMilliseconds, null)]);
        }
        catch (Exception ex)
        {
            watch.Stop();
            await transaction.RollbackAsync(CancellationToken.None);
            var safeError = ex is PostgresException pg ? $"{pg.SqlState}: {pg.MessageText}" : ex.Message;
            await connection.ExecuteAsync(new CommandDefinition(InsertHistorySql, new { entry.Name, Path = entry.Path, Checksum = checksum, UserId = userId, UserName = userName, Success = false, Duration = (int)watch.ElapsedMilliseconds, Error = safeError }, cancellationToken: ct));
            return Failure(entry.Name, entry.Path, safeError, (int)watch.ElapsedMilliseconds);
        }
    }

    public async Task<string> GetConsolidatedPendingScriptAsync(CancellationToken ct)
    {
        var plan = await GetPlanAsync(ct); var output = new StringBuilder("-- InovaGED: migrations obrigatórias pendentes\n-- Gerado em UTC: ").Append(DateTimeOffset.UtcNow).AppendLine("\n-- Execute após backup e revisão pelo DBA.\n");
        foreach (var item in plan.Items.Where(x => x.Required && !x.Applied)) { output.AppendLine($"\n-- BEGIN {item.Name} | SHA-256 {item.ChecksumSha256}"); var path = ResolveCatalogPath(item.Path); if (File.Exists(path)) output.AppendLine(await File.ReadAllTextAsync(path, ct)); else output.AppendLine("-- BLOQUEADO: arquivo não encontrado; nenhuma instrução foi gerada."); output.AppendLine($"-- END {item.Name}"); }
        return output.ToString();
    }

    private async Task<List<CatalogEntry>> ReadCatalogAsync(CancellationToken ct) => JsonSerializer.Deserialize<List<CatalogEntry>>(await File.ReadAllTextAsync(Path.Combine(_root, "database", "required_migrations.json"), ct), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidOperationException("Catálogo de migrations vazio.");
    private string ResolveCatalogPath(string path) { var full = Path.GetFullPath(Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar))); var migrations = Path.GetFullPath(Path.Combine(_root, "database", "migrations")) + Path.DirectorySeparatorChar; if (!full.StartsWith(migrations, StringComparison.Ordinal)) throw new InvalidOperationException("Path de migration fora do diretório permitido."); return full; }
    private static async Task<string> ChecksumAsync(string path, CancellationToken ct) { await using var stream = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant(); }
    private static string RemoveComments(string sql) => Regex.Replace(Regex.Replace(sql, @"/\*.*?\*/", "", RegexOptions.Singleline), @"--.*?$", "", RegexOptions.Multiline);
    private static string FindRoot(string start) { for (var d = new DirectoryInfo(start); d is not null; d = d.Parent) if (File.Exists(Path.Combine(d.FullName, "InovaGed.sln"))) return d.FullName; throw new DirectoryNotFoundException("Raiz do InovaGED não encontrada."); }
    private static DatabaseMigrationResult Failure(string name, string path, string error, int duration = 0) => new(false, error, [new(name, path, false, duration, error)]);
    private static Task EnsureHistoryAsync(NpgsqlConnection c, CancellationToken ct) => c.ExecuteAsync(new CommandDefinition(HistorySql, cancellationToken: ct));
    private const string InsertHistorySql = """insert into ged.schema_migration_history(script_name,script_path,checksum_sha256,applied_by,applied_by_name,source,success,duration_ms,error_message) values (@Name,@Path,@Checksum,@UserId,@UserName,'RUNNER',@Success,@Duration,@Error)""";
    private const string HistorySql = """
        create schema if not exists ged; create extension if not exists pgcrypto;
        create table if not exists ged.schema_migration_history(id uuid primary key default gen_random_uuid(),script_name text not null,script_path text null,checksum_sha256 text null,applied_at timestamptz not null default now(),applied_by uuid null,applied_by_name text null,source varchar(40) not null default 'MANUAL',success boolean not null default true,duration_ms integer null,error_message text null,notes text null,reg_status char(1) not null default 'A');
        alter table ged.schema_migration_history add column if not exists script_path text null;
        alter table ged.schema_migration_history add column if not exists checksum_sha256 text null;
        alter table ged.schema_migration_history add column if not exists applied_by uuid null;
        alter table ged.schema_migration_history add column if not exists applied_by_name text null;
        alter table ged.schema_migration_history add column if not exists source varchar(40) not null default 'MANUAL';
        alter table ged.schema_migration_history add column if not exists success boolean not null default true;
        alter table ged.schema_migration_history add column if not exists duration_ms integer null;
        alter table ged.schema_migration_history add column if not exists error_message text null;
        alter table ged.schema_migration_history add column if not exists notes text null;
        alter table ged.schema_migration_history add column if not exists reg_status char(1) not null default 'A';
        create unique index if not exists ux_schema_migration_history_script_success on ged.schema_migration_history(script_name) where success=true and reg_status='A';
        create index if not exists ix_schema_migration_history_applied_at on ged.schema_migration_history(applied_at desc);
        """;
    private sealed record CatalogEntry(string Name, string Path, string Area, bool Required, string Description);
    private sealed class HistoryRow { public string ScriptName { get; init; } = ""; public string? ChecksumSha256 { get; init; } public bool Success { get; init; } public DateTimeOffset AppliedAt { get; init; } public string? ErrorMessage { get; init; } }
}
