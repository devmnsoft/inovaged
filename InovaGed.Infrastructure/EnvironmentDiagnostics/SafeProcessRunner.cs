using System.Diagnostics;
using System.Text;
using InovaGed.Application.EnvironmentDiagnostics;

namespace InovaGed.Infrastructure.EnvironmentDiagnostics;

public sealed class SafeProcessRunner(ISafeMetadataSanitizer sanitizer, int outputLimitBytes = 65_536) : IProcessRunner
{
    public async Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
        if (request.Timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(request));
        var startedAt = Stopwatch.StartNew();
        using var process = new Process { StartInfo = CreateStartInfo(request) };
        try
        {
            if (!process.Start()) return Failure("PROCESS_NOT_STARTED", startedAt.Elapsed);
            using var timeout = new CancellationTokenSource(request.Timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            var stdout = ReadLimitedAsync(process.StandardOutput, linked.Token);
            var stderr = ReadLimitedAsync(process.StandardError, linked.Token);
            try { await process.WaitForExitAsync(linked.Token); }
            catch (OperationCanceledException)
            {
                TryKill(process);
                if (cancellationToken.IsCancellationRequested) throw;
                return new(true, true, null, sanitizer.SanitizeText(await SafeAwait(stdout)),
                    sanitizer.SanitizeText(await SafeAwait(stderr)), startedAt.Elapsed, "PROCESS_TIMEOUT");
            }
            return new(true, false, process.ExitCode, sanitizer.SanitizeText(await stdout),
                sanitizer.SanitizeText(await stderr), startedAt.Elapsed, process.ExitCode == 0 ? null : "PROCESS_NON_ZERO_EXIT");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        { return Failure("PROCESS_START_FAILED", startedAt.Elapsed); }
    }

    private static ProcessStartInfo CreateStartInfo(ProcessExecutionRequest request)
    {
        var info = new ProcessStartInfo(request.FileName) { RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = request.WorkingDirectory ?? string.Empty };
        foreach (var argument in request.Arguments) info.ArgumentList.Add(argument);
        if (request.EnvironmentVariables is not null)
            foreach (var variable in request.EnvironmentVariables) info.Environment[variable.Key] = variable.Value;
        return info;
    }

    private async Task<string> ReadLimitedAsync(StreamReader reader, CancellationToken token)
    {
        var buffer = new char[2048]; var result = new StringBuilder();
        while (result.Length < outputLimitBytes)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, outputLimitBytes - result.Length)), token);
            if (read == 0) break;
            result.Append(buffer, 0, read);
        }
        return result.ToString().Trim();
    }
    private static async Task<string> SafeAwait(Task<string> task) { try { return await task; } catch (OperationCanceledException) { return string.Empty; } }
    private static void TryKill(Process process) { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } }
    private static ProcessExecutionResult Failure(string code, TimeSpan duration) => new(false, false, null, "", "", duration, code);
}
