namespace InovaGed.Web.Ocr;

public static class OcrUiHelper
{
    public static DateTime ToConfiguredLocalTime(DateTime utc, string? timeZoneId)
    {
        var value = utc.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(utc, DateTimeKind.Utc) : utc.ToUniversalTime();
        if (string.IsNullOrWhiteSpace(timeZoneId)) return value.ToLocalTime();
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return TimeZoneInfo.ConvertTimeFromUtc(value, tz);
        }
        catch
        {
            return value.ToLocalTime();
        }
    }

    public static string BadgeClass(bool isAvailable, string? status)
    {
        if (isAvailable && IsCompleted(status)) return "ged-badge-ok";
        if (IsError(status)) return "ged-badge-danger";
        if (IsPending(status) || IsProcessing(status)) return "ged-badge-warning";
        return "ged-badge-muted";
    }

    public static string AvailabilityLabel(bool isAvailable, string? status, bool hasText)
    {
        var completed = IsCompleted(status);
        if (completed && isAvailable && hasText) return "OCR disponível";
        if (completed && !hasText) return "OCR concluído sem texto";
        if (IsProcessing(status)) return "OCR em processamento";
        if (IsPending(status)) return "OCR pendente";
        if (IsError(status)) return "OCR com erro";
        if (IsCancelled(status)) return "OCR cancelado";
        return "Sem OCR";
    }
    public static string FriendlyMessage(string? status, string? errorMessage)
    {
        if (string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
            return "OCR concluído com sucesso.";

        if (string.Equals(status, "PROCESSING", StringComparison.OrdinalIgnoreCase))
            return "Processando OCR…";

        if (string.Equals(status, "PENDING", StringComparison.OrdinalIgnoreCase))
            return "OCR na fila…";

        if (string.Equals(status, "ERROR", StringComparison.OrdinalIgnoreCase))
        {
            // mapa simples (pode evoluir depois pra error_code)
            var raw = (errorMessage ?? "").ToLowerInvariant();

            if (raw.Contains("timeout") || raw.Contains("time out"))
                return "Não foi possível processar o OCR a tempo. Tente novamente.";

            if (raw.Contains("unsupported") || raw.Contains("não suportado") || raw.Contains("not supported"))
                return "Este formato de arquivo não suporta OCR.";

            return string.IsNullOrWhiteSpace(errorMessage)
                ? "Falha ao processar OCR. Você pode tentar novamente."
                : $"Falha ao processar OCR: {errorMessage}";
        }

        return "OCR não solicitado.";
    }

    public static bool CanReprocess(string? status)
        => IsError(status) || IsCompleted(status);

    private static bool IsCompleted(string? status)
        => string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase);

    private static bool IsPending(string? status)
        => string.Equals(status, "PENDING", StringComparison.OrdinalIgnoreCase);

    private static bool IsProcessing(string? status)
        => string.Equals(status, "PROCESSING", StringComparison.OrdinalIgnoreCase);

    private static bool IsError(string? status)
        => string.Equals(status, "ERROR", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, "FAILED", StringComparison.OrdinalIgnoreCase);

    private static bool IsCancelled(string? status)
        => string.Equals(status, "CANCELLED", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, "CANCELED", StringComparison.OrdinalIgnoreCase);
}
