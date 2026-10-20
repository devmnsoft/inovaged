namespace InovaGed.Application.Ged.Processing;

public sealed class ProcessingStage
{
    public string Name { get; set; } = "";
    public string State { get; set; } = "";
}

public sealed record ProcessingCenterCriteria(
    string StatusGroup,
    string? Lane,
    string? NameLike,
    DateTimeOffset From,
    DateTimeOffset To,
    Guid? UserId,
    Guid? DocumentTypeId,
    IReadOnlyList<string> Statuses);

public static class ProcessingCenterCatalog
{
    public const int MaxWindowDays = 90;
    public const int DefaultWindowDays = 7;
    public const int MaxRows = 200;
    public const int MaxBatchReprocess = 20;

    public static ProcessingCenterCriteria Normalize(
        string? status,
        string? lane,
        string? name,
        DateTime? from,
        DateTime? to,
        string? userId,
        string? documentTypeId,
        DateTimeOffset now)
    {
        var group = (status ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "pending" or "aguardando" => "pending",
            "processing" or "andamento" => "processing",
            "error" or "failed" or "erro" or "falha" => "failed",
            "completed" or "concluido" or "concluído" => "completed",
            "cancelled" or "canceled" or "cancelado" => "cancelled",
            "all" or "todos" => "all",
            "running" or "execucao" or "execução" => "running",
            _ => "attention"
        };

        var start = from.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc))
            : now.AddDays(-DefaultWindowDays);
        var end = to.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(to.Value.Date.AddDays(1), DateTimeKind.Utc))
            : now.AddDays(1);
        if (end <= start)
            end = start.AddDays(1);
        if (end - start > TimeSpan.FromDays(MaxWindowDays))
            start = end.AddDays(-MaxWindowDays);

        var cleanName = SanitizeName(name);
        return new ProcessingCenterCriteria(
            group,
            NormalizeLane(lane),
            cleanName is null ? null : "%" + cleanName + "%",
            start,
            end,
            ParseId(userId),
            ParseId(documentTypeId),
            StatusesFor(group));
    }

    public static string Headline(string? status, int attempts, int maxAttempts)
    {
        var code = (status ?? string.Empty).Trim().ToUpperInvariant();
        return code switch
        {
            "PENDING" or "NOT_READY" => "Aguardando",
            "PROCESSING" or "RECEIVING" or "COMPLETING" or "OPEN" => "Em processamento",
            "READY" or "COMPLETED" => "Concluído",
            "CANCELED" or "CANCELLED" => "Cancelado",
            "FAILED" or "ERROR" or "FAILURE" => maxAttempts > 0 && attempts >= maxAttempts
                ? "Falha definitiva"
                : "Falha recuperável",
            _ => "Em andamento"
        };
    }

    public static string LaneLabel(string? lane) => (lane ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "PREVIEW" => "Visualização",
        "OCR" => "OCR",
        "CLASSIFICATION" => "Classificação",
        "SMART_INDEX" => "Indexação",
        "QUALITY" => "Qualidade",
        "SECTOR_EXTRACT" => "Extração",
        "UPLOAD" => "Upload",
        _ => "Processamento"
    };

    public static bool CanReprocess(string? source, string? status)
    {
        if (!string.Equals(source, "JOB", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(source, "OCR", StringComparison.OrdinalIgnoreCase))
            return false;

        var code = (status ?? string.Empty).Trim().ToUpperInvariant();
        return code is "FAILED" or "ERROR" or "FAILURE" or "CANCELLED" or "CANCELED";
    }

    public static bool CanCancel(string? source, string? status)
    {
        if (string.Equals(source, "UPLOAD", StringComparison.OrdinalIgnoreCase))
            return false;

        var code = (status ?? string.Empty).Trim().ToUpperInvariant();
        return code is "PENDING" or "PROCESSING" or "OPEN" or "RECEIVING" or "COMPLETING";
    }

    public static bool IncludesJobs(string? lane)
        => lane is null || !string.Equals(lane, "UPLOAD", StringComparison.OrdinalIgnoreCase);

    public static bool IncludesOcrTable(string? lane)
        => lane is null || string.Equals(lane, "OCR", StringComparison.OrdinalIgnoreCase);

    public static bool IncludesUploads(string? lane)
        => lane is null || string.Equals(lane, "UPLOAD", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<ProcessingStage> Timeline(string? lane, string? status, string? fileName, string? errorMessage)
    {
        var kind = (lane ?? string.Empty).Trim().ToUpperInvariant();
        var code = (status ?? string.Empty).Trim().ToUpperInvariant();
        var failed = code is "FAILED" or "ERROR" or "FAILURE";
        var done = code is "READY" or "COMPLETED";
        var canceled = code is "CANCELED" or "CANCELLED";
        var active = code is "PROCESSING" or "RECEIVING" or "COMPLETING";
        var conversionFailed = failed && IsConversionMessage(errorMessage);
        var needsConversion = NeedsConversion(fileName);

        string At(int index)
        {
            if (canceled)
                return index == 0 ? "Concluída" : "Dispensada";

            if (kind is "UPLOAD")
                return index == 0 ? JobState(done, failed, active) : "Dispensada";

            if (kind is "PREVIEW")
            {
                if (index == 0) return "Concluída";
                if (index == 1) return JobState(done, failed, active);
                if (index == 4) return done ? "Concluída" : failed ? "Falhou" : "Aguardando";
                return "Dispensada";
            }

            if (kind is "OCR")
            {
                if (index == 0) return "Concluída";
                if (index == 1)
                {
                    if (conversionFailed) return "Falhou";
                    if (!needsConversion) return "Dispensada";
                    if (done || failed) return "Concluída";
                    return active ? "Em andamento" : "Aguardando";
                }
                if (index == 2)
                {
                    if (conversionFailed) return "Aguardando";
                    return JobState(done, failed, active);
                }
                if (index == 4) return done ? "Concluída" : "Aguardando";
                return "Dispensada";
            }

            if (kind is "CLASSIFICATION" or "SMART_INDEX")
            {
                if (index == 0) return "Concluída";
                if (index == 3) return JobState(done, failed, active);
                if (index == 4) return done ? "Concluída" : "Aguardando";
                return "Dispensada";
            }

            if (index == 0) return "Concluída";
            if (index == 4) return done ? "Concluída" : failed ? "Falhou" : "Aguardando";
            return "Dispensada";
        }

        string[] names = ["Recebimento", "Conversão", "OCR", "Indexação", "Disponível"];
        return names
            .Select((name, index) => new ProcessingStage { Name = name, State = At(index) })
            .ToArray();
    }

    public static bool NeedsConversion(string? fileName)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        return extension is ".doc" or ".docx" or ".docm" or ".odt" or ".rtf"
            or ".xls" or ".xlsx" or ".xlsm" or ".ods" or ".csv"
            or ".ppt" or ".pptx" or ".odp" or ".odg";
    }

    public static bool IsConversionMessage(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
            return false;

        return errorMessage.Contains("conversão", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("tempo esperado", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("protegido por senha", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("não é compatível", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("não pôde ser validado", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("corrompido", StringComparison.OrdinalIgnoreCase);
    }

    private static string JobState(bool done, bool failed, bool active)
    {
        if (done) return "Concluída";
        if (failed) return "Falhou";
        if (active) return "Em andamento";
        return "Aguardando";
    }

    private static IReadOnlyList<string> StatusesFor(string group) => group switch
    {
        "pending" => ["PENDING", "OPEN"],
        "processing" => ["PROCESSING", "RECEIVING", "COMPLETING"],
        "running" => ["PENDING", "OPEN", "PROCESSING", "RECEIVING", "COMPLETING"],
        "failed" => ["FAILED", "ERROR", "FAILURE"],
        "completed" => ["COMPLETED", "READY"],
        "cancelled" => ["CANCELLED", "CANCELED"],
        "all" => [],
        _ => ["PENDING", "OPEN", "PROCESSING", "RECEIVING", "COMPLETING", "FAILED", "ERROR", "FAILURE"]
    };

    private static string? NormalizeLane(string? lane)
    {
        var value = (lane ?? string.Empty).Trim().ToUpperInvariant();
        return value switch
        {
            "PREVIEW" or "OCR" or "CLASSIFICATION" or "SMART_INDEX" or "QUALITY" or "SECTOR_EXTRACT" or "UPLOAD" => value,
            _ => null
        };
    }

    private static string? SanitizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var cleaned = name.Trim().Replace("%", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal).Replace("\\", string.Empty, StringComparison.Ordinal);
        if (cleaned.Length > 80)
            cleaned = cleaned[..80];
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private static Guid? ParseId(string? value)
        => Guid.TryParse(value, out var id) && id != Guid.Empty ? id : null;
}
