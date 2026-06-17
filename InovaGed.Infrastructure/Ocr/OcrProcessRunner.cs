using System.Diagnostics;
using System.Text;
using InovaGed.Application.Ocr;

namespace InovaGed.Infrastructure.Ocr;

public interface IOcrProcessRunner
{
    Task<OcrProcessResult> RunVersionAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, IDictionary<string, string> environment, TimeSpan timeout, CancellationToken ct);
}

public sealed class OcrProcessRunner : IOcrProcessRunner
{
    public async Task<OcrProcessResult> RunVersionAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, IDictionary<string, string> environment, TimeSpan timeout, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(fileName))
            return new(null, string.Empty, string.Empty, false, 0, "Caminho do executável não configurado.");
        if (Path.IsPathRooted(fileName) && !File.Exists(fileName))
            return new(null, string.Empty, string.Empty, false, 0, $"Arquivo não encontrado: {fileName}");

        try
        {
            var psi = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? AppContext.BaseDirectory : workingDirectory,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var arg in arguments) psi.ArgumentList.Add(arg);
            foreach (var item in environment) psi.Environment[item.Key] = item.Value;

            using var process = Process.Start(psi);
            if (process is null) return new(null, string.Empty, string.Empty, false, sw.ElapsedMilliseconds, "Falha ao iniciar processo.");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { if (!process.HasExited) process.Kill(true); } catch { }
                return new(null, await stdoutTask, await stderrTask, true, sw.ElapsedMilliseconds, "Tempo limite excedido ao executar comando de versão.");
            }
            return new(process.ExitCode, await stdoutTask, await stderrTask, false, sw.ElapsedMilliseconds, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(null, string.Empty, string.Empty, false, sw.ElapsedMilliseconds, ex.Message);
        }
    }
}
