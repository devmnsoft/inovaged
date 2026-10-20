namespace InovaGed.Application.Common.Preview;

public static class LibreOfficeCommand
{
    public const int HardTimeoutCapMinutes = 15;

    public static string ResolveExecutable(string configuredPath, Func<string, bool> exists)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return "";

        var configured = configuredPath.Trim();
        var directory = Path.GetDirectoryName(configured);
        var name = Path.GetFileName(configured);
        if (string.IsNullOrWhiteSpace(directory) || !name.Equals("soffice.exe", StringComparison.OrdinalIgnoreCase))
            return configured;

        var consoleHost = Path.Combine(directory, "soffice.com");
        if (exists(consoleHost))
            return consoleHost;

        var binary = Path.Combine(directory, "soffice.bin");
        return exists(binary) ? binary : configured;
    }

    public static string ExportFilter(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return extension.ToLowerInvariant() switch
        {
            ".xls" or ".xlsx" or ".xlsm" or ".ods" or ".csv" => "pdf:calc_pdf_Export",
            ".ppt" or ".pptx" or ".odp" => "pdf:impress_pdf_Export",
            ".odg" => "pdf:draw_pdf_Export",
            _ => "pdf:writer_pdf_Export"
        };
    }

    public static IReadOnlyList<string> Arguments(string profileUri, string outputDirectory, string inputPath, string originalFileName)
        =>
        [
            "--headless",
            "--nologo",
            "--nofirststartwizard",
            "--norestore",
            "--nodefault",
            "--nolockcheck",
            "--invisible",
            $"-env:UserInstallation={profileUri}",
            "--convert-to",
            ExportFilter(originalFileName),
            "--outdir",
            outputDirectory,
            inputPath
        ];

    public static TimeSpan TimeoutFor(LibreOfficeOptions options, long sizeBytes)
    {
        var baseline = options.GetTimeout();
        var configuredCap = options.MaxTimeoutMinutes > 0 ? options.MaxTimeoutMinutes : 8;
        var cap = TimeSpan.FromMinutes(Math.Clamp(configuredCap, 2, HardTimeoutCapMinutes));
        if (baseline > cap)
            baseline = cap;
        if (sizeBytes <= 5 * 1024 * 1024)
            return baseline;

        var extraMinutes = ((sizeBytes - (5 * 1024 * 1024)) / (10 * 1024 * 1024)) + 1;
        var scaled = baseline + TimeSpan.FromMinutes(extraMinutes);
        return scaled > cap ? cap : scaled;
    }

    public static bool AcceptOutput(LibreOfficeProcessRun run, bool validPdf)
    {
        if (!run.Started || run.Canceled || !validPdf)
            return false;

        if (run.Exited && run.ExitCode == 0)
            return true;

        return run.TimedOut;
    }
}

public sealed class DocumentConversionException : Exception
{
    public const string TimeoutMessage = "Não foi possível concluir a conversão deste documento no tempo esperado. O arquivo original está preservado e uma nova tentativa será realizada automaticamente, quando aplicável.";
    public const string ProtectedMessage = "Este arquivo está protegido por senha e não pode ser processado automaticamente.";
    public const string UnsupportedMessage = "O formato deste documento não é compatível com a conversão automática.";
    public const string UnavailableMessage = "O serviço de conversão está temporariamente indisponível. O documento permanece armazenado.";
    public const string DefinitiveMessage = "Não foi possível processar o documento após as tentativas permitidas. Solicite uma análise técnica ou envie uma nova versão.";
    public const string InterruptedMessage = "O processamento deste documento foi interrompido. O arquivo original está preservado e uma nova tentativa será realizada automaticamente, quando aplicável.";
    public const string OcrTimeoutMessage = "Não foi possível concluir o OCR deste documento no tempo esperado. O arquivo original está preservado e uma nova tentativa será realizada automaticamente, quando aplicável.";

    public string FailureCode { get; }
    public string UserMessage { get; }
    public bool Transient { get; }

    public DocumentConversionException(string failureCode, string userMessage, bool transientFailure, string technicalMessage, Exception? inner = null)
        : base(technicalMessage, inner)
    {
        FailureCode = failureCode;
        UserMessage = userMessage;
        Transient = transientFailure;
    }

    public static DocumentConversionException FromProcess(string? stdErr, int? exitCode, bool timedOut, bool started, bool exited)
    {
        if (!started)
            return new("CONVERSION_START_FAILED", UnavailableMessage, true, "O processo de conversão não iniciou.");
        if (timedOut || !exited)
            return new("CONVERSION_TIMEOUT", TimeoutMessage, true, "A conversão excedeu o tempo permitido.");
        if (LooksProtected(stdErr))
            return new("CONVERSION_PROTECTED", ProtectedMessage, false, "O conversor recusou um arquivo protegido.");
        if (LooksUnsupported(stdErr))
            return new("CONVERSION_UNSUPPORTED", UnsupportedMessage, false, "O conversor não reconheceu o formato.");
        if (LooksCorrupt(stdErr))
            return new("CONVERSION_CORRUPT", "O arquivo está corrompido e não pode ser convertido automaticamente.", false, "O conversor rejeitou o arquivo.");
        return new("CONVERSION_EXIT", UnavailableMessage, true, $"A conversão encerrou com código {exitCode ?? -1}.");
    }

    public static bool LooksProtected(string? text)
        => Contains(text, "password") || Contains(text, "encrypted") || Contains(text, "senha");

    public static bool LooksUnsupported(string? text)
        => Contains(text, "no export filter") || Contains(text, "unknown document format") || Contains(text, "formato");

    public static bool LooksCorrupt(string? text)
        => Contains(text, "corrupt") || Contains(text, "damaged") || Contains(text, "corromp");

    private static bool Contains(string? text, string value)
        => !string.IsNullOrWhiteSpace(text) && text.Contains(value, StringComparison.OrdinalIgnoreCase);
}

public readonly record struct OcrConversionDecision(bool Retry, string UserMessage, int Attempts, TimeSpan Delay);

public static class OcrConversionPolicy
{
    public const int MaxAttempts = 3;

    public static OcrConversionDecision Decide(DocumentConversionException failure, int attemptsAlreadyRecorded)
    {
        var attempts = attemptsAlreadyRecorded + 1;
        if (!failure.Transient || attempts >= MaxAttempts)
        {
            var message = failure.Transient ? DocumentConversionException.DefinitiveMessage : failure.UserMessage;
            return new(false, message, attempts, TimeSpan.Zero);
        }

        var seconds = attempts switch { 1 => 30, 2 => 120, _ => 300 };
        var jitter = Random.Shared.Next(-(seconds / 5), (seconds / 5) + 1);
        return new(true, failure.UserMessage, attempts, TimeSpan.FromSeconds(Math.Max(5, seconds + jitter)));
    }
}
