namespace InovaGed.Application.Ged.Processing;

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
