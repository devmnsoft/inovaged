using InovaGed.Application.Common.Preview;

namespace InovaGed.Application.Tests.Preview;

public sealed class LibreOfficeConversionTests
{
    [Fact]
    public void Windows_launcher_prefers_the_console_host_beside_soffice_exe()
    {
        var root = Directory.CreateTempSubdirectory("lo-resolve-");
        try
        {
            var exe = Path.Combine(root.FullName, "soffice.exe");
            var console = Path.Combine(root.FullName, "soffice.com");
            File.WriteAllText(exe, "exe");
            File.WriteAllText(console, "com");

            var resolved = LibreOfficeCommand.ResolveExecutable(exe, File.Exists);

            Assert.Equal(console, resolved);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Fact]
    public void Arguments_use_the_isolated_profile_and_keep_special_names_intact()
    {
        var input = Path.Combine("C:\\entrada", "guia clínica (1).xlsx");
        var arguments = LibreOfficeCommand.Arguments("file:///C:/perfil/isolado/", "C:\\saida", input, "guia clínica (1).xlsx");

        Assert.Contains("-env:UserInstallation=file:///C:/perfil/isolado/", arguments);
        Assert.DoesNotContain(arguments, argument => argument.StartsWith("--env:", StringComparison.Ordinal));
        Assert.Equal("pdf:calc_pdf_Export", arguments[9]);
        Assert.Equal(input, arguments[^1]);
        Assert.Equal("pdf:writer_pdf_Export", LibreOfficeCommand.ExportFilter("termo.docx"));
        Assert.Equal("pdf:impress_pdf_Export", LibreOfficeCommand.ExportFilter("apresentação.pptx"));
    }

    [Fact]
    public void Timeout_grows_with_size_and_stays_inside_the_cap()
    {
        var options = new LibreOfficeOptions { TimeoutMinutes = 2, MaxTimeoutMinutes = 99 };
        var small = LibreOfficeCommand.TimeoutFor(options, 1024);
        var large = LibreOfficeCommand.TimeoutFor(options, 80L * 1024 * 1024);

        Assert.Equal(TimeSpan.FromMinutes(2), small);
        Assert.Equal(TimeSpan.FromMinutes(10), large);
        Assert.True(large <= TimeSpan.FromMinutes(LibreOfficeCommand.HardTimeoutCapMinutes));
        Assert.Equal(TimeSpan.FromMinutes(LibreOfficeCommand.HardTimeoutCapMinutes), LibreOfficeCommand.TimeoutFor(options, 200L * 1024 * 1024));
        Assert.True(LibreOfficeCommand.AcceptOutput(new LibreOfficeProcessRun { Started = true, TimedOut = true, Exited = true }, true));
        Assert.False(LibreOfficeCommand.AcceptOutput(new LibreOfficeProcessRun { Started = true, TimedOut = true, Exited = true }, false));
        Assert.False(LibreOfficeCommand.AcceptOutput(new LibreOfficeProcessRun { Started = true, Canceled = true, TimedOut = false }, true));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void Timeout_retries_until_the_attempt_limit(int recordedAttempts, bool retry)
    {
        var failure = new DocumentConversionException("CONVERSION_TIMEOUT", DocumentConversionException.TimeoutMessage, true, "timeout");
        var decision = OcrConversionPolicy.Decide(failure, recordedAttempts);

        Assert.Equal(retry, decision.Retry);
        Assert.Equal(retry ? DocumentConversionException.TimeoutMessage : DocumentConversionException.DefinitiveMessage, decision.UserMessage);
    }

    [Fact]
    public void Protected_file_is_permanent_on_the_first_attempt()
    {
        var failure = DocumentConversionException.FromProcess("Document is password protected", 1, false, true, true);
        var decision = OcrConversionPolicy.Decide(failure, 0);

        Assert.False(decision.Retry);
        Assert.Equal(DocumentConversionException.ProtectedMessage, decision.UserMessage);
        Assert.Equal(DocumentConversionException.UnsupportedMessage, DocumentConversionException.FromProcess("unknown document format", 1, false, true, true).UserMessage);
    }

    [Fact]
    public async Task Timeout_stops_only_the_started_process_tree()
    {
        var work = Directory.CreateTempSubdirectory("lo-proc-");
        try
        {
            var run = await LibreOfficeProcessRunner.RunAsync(
                "powershell.exe",
                ["-NoProfile", "-Command", "Start-Sleep -Seconds 30"],
                work.FullName,
                TimeSpan.FromSeconds(1),
                CancellationToken.None);

            Assert.True(run.Started);
            Assert.True(run.TimedOut);
            Assert.True(run.Exited);
            Assert.NotNull(run.ProcessId);
            Assert.False(IsAlive(run.ProcessId!.Value));
        }
        finally
        {
            DeleteWhenFree(work);
        }
    }

    [Fact]
    public async Task Cancellation_is_not_reported_as_timeout()
    {
        var work = Directory.CreateTempSubdirectory("lo-cancel-");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        try
        {
            var run = await LibreOfficeProcessRunner.RunAsync(
                "powershell.exe",
                ["-NoProfile", "-Command", "Start-Sleep -Seconds 30"],
                work.FullName,
                TimeSpan.FromSeconds(30),
                cts.Token);

            Assert.True(run.Started);
            Assert.True(run.Canceled);
            Assert.False(run.TimedOut);
            Assert.NotNull(run.ProcessId);
            Assert.False(IsAlive(run.ProcessId!.Value));
        }
        finally
        {
            DeleteWhenFree(work);
        }
    }

    [Fact]
    public async Task Missing_executable_does_not_start()
    {
        var work = Directory.CreateTempSubdirectory("lo-missing-");
        try
        {
            var run = await LibreOfficeProcessRunner.RunAsync(
                Path.Combine(work.FullName, "soffice-ausente.exe"),
                ["--version"],
                work.FullName,
                TimeSpan.FromSeconds(2),
                CancellationToken.None);

            Assert.False(run.Started);
            Assert.False(run.TimedOut);
        }
        finally
        {
            DeleteWhenFree(work);
        }
    }

    [Fact]
    public async Task Probe_without_converter_hides_paths_and_skips_the_sample()
    {
        var options = new LibreOfficeOptions { SofficePath = Path.Combine(Path.GetTempPath(), "soffice-inexistente.exe") };
        var checks = await ConversionEnvironmentProbe.InspectAsync(options, CancellationToken.None);

        Assert.Contains(checks, check => check.Name == "LibreOffice" && !check.Success);
        Assert.Contains(checks, check => check.Name == "Conversão de teste" && check.Message.Contains("não foi executada", StringComparison.Ordinal));
        Assert.All(checks, check => Assert.DoesNotContain(":\\", check.Message ?? "", StringComparison.Ordinal));
        Assert.All(checks, check => Assert.True(string.IsNullOrWhiteSpace(check.Path)));
    }

    [Fact]
    public async Task Two_short_processes_both_finish()
    {
        var first = LibreOfficeProcessRunner.RunAsync("cmd.exe", ["/c", "exit", "0"], Path.GetTempPath(), TimeSpan.FromSeconds(10), CancellationToken.None);
        var second = LibreOfficeProcessRunner.RunAsync("cmd.exe", ["/c", "exit", "0"], Path.GetTempPath(), TimeSpan.FromSeconds(10), CancellationToken.None);
        var runs = await Task.WhenAll(first, second);

        Assert.All(runs, run => Assert.Equal(0, run.ExitCode));
        Assert.All(runs, run => Assert.False(run.TimedOut));
    }

    private static void DeleteWhenFree(DirectoryInfo work)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (work.Exists)
                    work.Delete(true);
                return;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < 4)
            {
                Thread.Sleep(200);
            }
        }
    }

    private static bool IsAlive(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
