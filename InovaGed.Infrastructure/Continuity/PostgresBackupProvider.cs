using System.Diagnostics;
using System.Security.Cryptography;
using InovaGed.Application.Continuity;
using InovaGed.Application.SystemHealth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InovaGed.Infrastructure.Continuity;

public interface IPostgresBackupProvider { Task<PostgresBackupResult> DumpAsync(string connectionString, string outputFile, CancellationToken ct); }
public sealed record PostgresBackupResult(bool Success, int ExitCode, string Sha256, string PgDumpVersion, string SanitizedError);

public sealed class PostgresBackupProvider(IExecutableResolver resolver, IOptions<BackupOptions> options, ILogger<PostgresBackupProvider> logger) : IPostgresBackupProvider
{
    public async Task<PostgresBackupResult> DumpAsync(string connectionString, string outputFile, CancellationToken ct)
    {
        var pgDumpResolution = resolver.Resolve("pg_dump", options.Value.PostgresBinPath, "PG_DUMP_PATH", []);
        var pgRestoreResolution = resolver.Resolve("pg_restore", options.Value.PostgresBinPath, "PG_RESTORE_PATH", []);
        if (!pgDumpResolution.IsAvailable || string.IsNullOrWhiteSpace(pgDumpResolution.Path)) throw new InvalidOperationException(pgDumpResolution.Message);
        if (!pgRestoreResolution.IsAvailable || string.IsNullOrWhiteSpace(pgRestoreResolution.Path)) throw new InvalidOperationException(pgRestoreResolution.Message);
        var pgDump = pgDumpResolution.Path;
        var pgRestore = pgRestoreResolution.Path;
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputFile)) ?? ".");
        var partialFile = outputFile + ".partial";
        var passFile = Path.Combine(Path.GetTempPath(), $"inovaged-pgpass-{Guid.NewGuid():N}");
        try
        {
            await File.WriteAllTextAsync(passFile, $"{builder.Host}:{builder.Port}:{builder.Database}:{builder.Username}:{builder.Password}\n", ct);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(passFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var psi = new ProcessStartInfo(pgDump) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
            psi.ArgumentList.Add("--format=custom"); psi.ArgumentList.Add("--no-owner"); psi.ArgumentList.Add("--no-privileges");
            psi.ArgumentList.Add($"--compress={options.Value.CompressionLevel}"); psi.ArgumentList.Add("--file"); psi.ArgumentList.Add(partialFile);
            psi.ArgumentList.Add("--host"); psi.ArgumentList.Add(builder.Host); psi.ArgumentList.Add("--port"); psi.ArgumentList.Add(builder.Port.ToString());
            psi.ArgumentList.Add("--username"); psi.ArgumentList.Add(builder.Username); psi.ArgumentList.Add(builder.Database);
            psi.Environment["PGPASSFILE"] = passFile;
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar pg_dump.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.Value.CommandTimeoutSeconds));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(linked.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(linked.Token);
            await process.WaitForExitAsync(linked.Token);
            var err = Mask(await stderrTask); _ = await stdoutTask;
            if (process.ExitCode != 0 || !File.Exists(partialFile)) { TryDelete(partialFile); return new(false, process.ExitCode, string.Empty, await VersionAsync(pgDump, ct), err); }
            File.Move(partialFile, outputFile, true);
            var verifyInfo = new ProcessStartInfo(pgRestore) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
            verifyInfo.ArgumentList.Add("--list"); verifyInfo.ArgumentList.Add(outputFile);
            var verify = Process.Start(verifyInfo);
            if (verify is null) return new(false, -1, string.Empty, await VersionAsync(pgDump, ct), "pg_restore não iniciou");
            await verify.WaitForExitAsync(ct);
            if (verify.ExitCode != 0) return new(false, verify.ExitCode, string.Empty, await VersionAsync(pgDump, ct), Mask(await verify.StandardError.ReadToEndAsync(ct)));
            await using var fs = File.OpenRead(outputFile); var hash = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct)).ToLowerInvariant();
            logger.LogInformation("Backup PostgreSQL concluído com senha mascarada. Database={Database} Sha256={Sha256}", builder.Database, hash);
            return new(true, 0, hash, await VersionAsync(pgDump, ct), string.Empty);
        }
        finally { TryDelete(passFile); TryDelete(partialFile); }
    }
    private static void TryDelete(string path) { try { if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path); } catch { } }
    private static string Mask(string value) => value.Replace("password", "p******d", StringComparison.OrdinalIgnoreCase).Replace("pwd", "p*d", StringComparison.OrdinalIgnoreCase);
    private static async Task<string> VersionAsync(string exe, CancellationToken ct){ using var p=Process.Start(new ProcessStartInfo(exe,"--version"){RedirectStandardOutput=true,UseShellExecute=false}); if(p is null) return "unknown"; await p.WaitForExitAsync(ct); return (await p.StandardOutput.ReadToEndAsync(ct)).Trim(); }
}
