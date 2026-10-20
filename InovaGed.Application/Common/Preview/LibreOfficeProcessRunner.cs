using System.Diagnostics;
using System.Text;

namespace InovaGed.Application.Common.Preview;

public sealed class LibreOfficeProcessRun
{
    public bool Started { get; init; }
    public bool Exited { get; init; }
    public bool TimedOut { get; init; }
    public bool Canceled { get; init; }
    public int? ExitCode { get; init; }
    public int? ProcessId { get; init; }
    public long ElapsedMs { get; init; }
    public string StdOut { get; init; } = "";
    public string StdErr { get; init; } = "";
}

public static class LibreOfficeProcessRunner
{
    public const int OutputLimit = 8192;
    public static readonly TimeSpan KillGrace = TimeSpan.FromSeconds(5);

    public static async Task<LibreOfficeProcessRun> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var start = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        Process? process;
        try
        {
            process = Process.Start(start);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new LibreOfficeProcessRun { ElapsedMs = watch.ElapsedMilliseconds, StdErr = ex.GetType().Name };
        }

        if (process is null)
            return new LibreOfficeProcessRun { ElapsedMs = watch.ElapsedMilliseconds };

        using (process)
        {
            try { process.StandardInput.Close(); } catch (IOException) { }

            using var readStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var stdout = ReadLimitedAsync(process.StandardOutput, readStop.Token);
            var stderr = ReadLimitedAsync(process.StandardError, readStop.Token);
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            wait.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(wait.Token);
            }
            catch (OperationCanceledException)
            {
                await StopTreeAsync(process);
                readStop.Cancel();
                return new LibreOfficeProcessRun
                {
                    Started = true,
                    ProcessId = SafePid(process),
                    Exited = SafeExited(process),
                    ExitCode = SafeExitCode(process),
                    TimedOut = !ct.IsCancellationRequested,
                    Canceled = ct.IsCancellationRequested,
                    StdOut = await ObserveAsync(stdout),
                    StdErr = await ObserveAsync(stderr),
                    ElapsedMs = watch.ElapsedMilliseconds
                };
            }

            await Task.WhenAny(Task.WhenAll(stdout, stderr), Task.Delay(TimeSpan.FromSeconds(2)));
            readStop.Cancel();
            return new LibreOfficeProcessRun
            {
                Started = true,
                Exited = true,
                ExitCode = process.ExitCode,
                ProcessId = SafePid(process),
                StdOut = await ObserveAsync(stdout),
                StdErr = await ObserveAsync(stderr),
                ElapsedMs = watch.ElapsedMilliseconds
            };
        }
    }

    private static async Task StopTreeAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        catch (NotSupportedException) { }

        try
        {
            using var grace = new CancellationTokenSource(KillGrace);
            await process.WaitForExitAsync(grace.Token);
        }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException) { }
    }

    private static async Task<string> ReadLimitedAsync(StreamReader reader, CancellationToken ct)
    {
        var text = new StringBuilder();
        var buffer = new char[1024];
        try
        {
            while (text.Length < OutputLimit)
            {
                var count = await reader.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, OutputLimit - text.Length)), ct);
                if (count == 0)
                    break;
                text.Append(buffer, 0, count);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        return text.ToString();
    }

    private static async Task<string> ObserveAsync(Task<string> read)
    {
        try { return await read; }
        catch (OperationCanceledException) { return ""; }
        catch (IOException) { return ""; }
    }

    private static int? SafePid(Process process)
    {
        try { return process.Id; }
        catch (InvalidOperationException) { return null; }
    }

    private static bool SafeExited(Process process)
    {
        try { return process.HasExited; }
        catch (InvalidOperationException) { return false; }
    }

    private static int? SafeExitCode(Process process)
    {
        try { return process.HasExited ? process.ExitCode : null; }
        catch (InvalidOperationException) { return null; }
    }
}
