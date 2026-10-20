using System.Diagnostics;
using InovaGed.Infrastructure.Ocr;

namespace InovaGed.Application.Tests;

public sealed class OcrProcessLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Timeout_and_cancellation_terminate_the_process(bool cancel)
    {
        var directory = Path.Combine(Path.GetTempPath(), "ocr espaço ação # " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var script = Path.Combine(directory, "processo teste.ps1");
        var pidPath = Path.Combine(directory, "pid.txt");
        await File.WriteAllTextAsync(script, "param([string]$PidPath)\n[IO.File]::WriteAllText($PidPath, [string]$PID)\nStart-Sleep -Seconds 120");
        using var cancellation = new CancellationTokenSource();
        try
        {
            var runner = new OcrProcessRunner();
            var command = OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";
            var task = runner.RunVersionAsync(command, ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, pidPath],
                directory, new Dictionary<string, string>(), TimeSpan.FromSeconds(10), cancellation.Token);
            var ready = Stopwatch.StartNew();
            while (!File.Exists(pidPath) && ready.Elapsed < TimeSpan.FromSeconds(8)) await Task.Delay(50);
            Assert.True(File.Exists(pidPath), "O processo de teste não iniciou.");
            var pid = int.Parse(await File.ReadAllTextAsync(pidPath));
            if (cancel)
            {
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            }
            else
            {
                var result = await task;
                Assert.True(result.TimedOut);
            }
            try
            {
                using var process = Process.GetProcessById(pid);
                Assert.True(process.HasExited || process.WaitForExit(3000), "O processo sobreviveu ao cancelamento/timeout.");
            }
            catch (ArgumentException) { /* PID is already gone. */ }
        }
        finally
        {
            // Process termination is asserted above. Windows antivirus/indexing can briefly
            // retain a directory handle after exit; that must not mask the lifetime assertion.
            try { Directory.Delete(directory, true); }
            catch (IOException) { }
        }
    }
}
