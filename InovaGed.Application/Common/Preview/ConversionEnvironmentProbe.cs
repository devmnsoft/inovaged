using InovaGed.Application.Ocr;

namespace InovaGed.Application.Common.Preview;

public static class ConversionEnvironmentProbe
{
    public static async Task<IReadOnlyList<OcrEnvironmentCheckResult>> InspectAsync(LibreOfficeOptions options, CancellationToken ct)
    {
        var checks = new List<OcrEnvironmentCheckResult>();
        var configured = options.GetResolvedSofficePath();
        var resolved = LibreOfficeCommand.ResolveExecutable(configured, File.Exists);
        var installed = !string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved);
        checks.Add(new OcrEnvironmentCheckResult
        {
            Name = "LibreOffice",
            ConfigKey = "Preview:SofficePath",
            Required = false,
            Exists = installed,
            CanExecute = installed,
            Success = installed,
            Message = installed
                ? "Executável de conversão localizado para o usuário do serviço."
                : DocumentConversionException.UnavailableMessage,
            Suggestion = installed ? null : "Instale o LibreOffice e confirme a configuração do serviço. O caminho interno não é exibido aqui."
        });

        checks.Add(InspectTemporarySpace());
        if (!installed)
        {
            checks.Add(SkippedConversion());
            return checks;
        }

        var version = await RunVersionAsync(resolved, ct);
        checks[0].CanExecute = version.ExitCode == 0 && version.Exited && !version.TimedOut;
        checks[0].Success = checks[0].CanExecute;
        checks[0].ExitCode = version.ExitCode;
        checks[0].ElapsedMs = version.ElapsedMs;
        checks[0].Message = checks[0].Success
            ? "O conversor respondeu ao comando de versão."
            : DocumentConversionException.UnavailableMessage;
        if (!checks[0].Success)
        {
            checks.Add(SkippedConversion());
            return checks;
        }

        checks.Add(await RunSyntheticConversionAsync(resolved, ct));
        return checks;
    }

    private static OcrEnvironmentCheckResult InspectTemporarySpace()
    {
        try
        {
            var probe = Directory.CreateTempSubdirectory("ig-conv-probe-");
            var file = Path.Combine(probe.FullName, ".write-probe");
            File.WriteAllText(file, "ok");
            File.Delete(file);
            var root = Path.GetPathRoot(probe.FullName);
            long freeMb = -1;
            if (!string.IsNullOrWhiteSpace(root))
            {
                var drive = new DriveInfo(root);
                if (drive.IsReady)
                    freeMb = drive.AvailableFreeSpace / (1024 * 1024);
            }
            probe.Delete(true);
            var enough = freeMb < 0 || freeMb >= 50;
            return new OcrEnvironmentCheckResult
            {
                Name = "Espaço temporário",
                ConfigKey = "TEMP",
                Required = false,
                Exists = true,
                CanExecute = true,
                Success = enough,
                Message = freeMb < 0
                    ? "O diretório temporário aceita gravação. O espaço livre não foi medido."
                    : $"Espaço livre para conversão: {freeMb} MB.",
                Suggestion = enough ? null : "Libere espaço no volume temporário antes de reprocessar conversões."
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new OcrEnvironmentCheckResult
            {
                Name = "Espaço temporário",
                ConfigKey = "TEMP",
                Required = false,
                Exists = false,
                Success = false,
                Message = "O diretório temporário da conversão não está gravável.",
                Suggestion = "Conceda gravação ao usuário do serviço no diretório temporário."
            };
        }
    }

    private static async Task<LibreOfficeProcessRun> RunVersionAsync(string executable, CancellationToken ct)
    {
        var profile = Directory.CreateTempSubdirectory("ig-conv-version-");
        try
        {
            return await LibreOfficeProcessRunner.RunAsync(
                executable,
                ["--headless", "--nologo", "--nofirststartwizard", $"-env:UserInstallation={new Uri(profile.FullName).AbsoluteUri}", "--version"],
                profile.FullName,
                TimeSpan.FromSeconds(8),
                ct);
        }
        finally
        {
            TryDelete(profile);
        }
    }

    private static async Task<OcrEnvironmentCheckResult> RunSyntheticConversionAsync(string executable, CancellationToken ct)
    {
        var root = Directory.CreateTempSubdirectory("ig-conv-synthetic-");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var input = Path.Combine(root.FullName, "amostra.csv");
            var output = Path.Combine(root.FullName, "saida");
            var profile = Path.Combine(root.FullName, "perfil");
            Directory.CreateDirectory(output);
            Directory.CreateDirectory(profile);
            await File.WriteAllTextAsync(input, "coluna\n1\n", ct);
            var run = await LibreOfficeProcessRunner.RunAsync(
                executable,
                LibreOfficeCommand.Arguments(new Uri(profile).AbsoluteUri, output, input, "amostra.csv"),
                root.FullName,
                TimeSpan.FromSeconds(20),
                ct);
            var pdf = Directory.Exists(output) ? Directory.EnumerateFiles(output, "*.pdf").FirstOrDefault() : null;
            var valid = pdf is not null && new FileInfo(pdf).Length >= 5;
            return new OcrEnvironmentCheckResult
            {
                Name = "Conversão de teste",
                ConfigKey = "Preview",
                Required = false,
                Exists = true,
                CanExecute = run.Started,
                Success = valid && run.ExitCode == 0 && !run.TimedOut,
                ExitCode = run.ExitCode,
                ElapsedMs = watch.ElapsedMilliseconds,
                Message = valid && run.ExitCode == 0
                    ? $"Arquivo sintético convertido em {watch.ElapsedMilliseconds} ms."
                    : DocumentConversionException.UnavailableMessage,
                Suggestion = valid ? null : "A amostra sintética não produziu PDF. Nenhum documento real foi usado."
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new OcrEnvironmentCheckResult
            {
                Name = "Conversão de teste",
                ConfigKey = "Preview",
                Required = false,
                Success = false,
                ElapsedMs = watch.ElapsedMilliseconds,
                Message = DocumentConversionException.UnavailableMessage,
                Suggestion = "A amostra sintética não pôde ser gravada."
            };
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static OcrEnvironmentCheckResult SkippedConversion() => new()
    {
        Name = "Conversão de teste",
        ConfigKey = "Preview",
        Required = false,
        Success = false,
        Message = "A conversão de teste não foi executada porque o conversor não está disponível.",
        Suggestion = "Nenhum documento real é usado nesta verificação."
    };

    private static void TryDelete(DirectoryInfo directory)
    {
        try { if (directory.Exists) directory.Delete(true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
