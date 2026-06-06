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
        if (isAvailable) return "ged-badge-ok";
        if (string.Equals(status, "ERROR", StringComparison.OrdinalIgnoreCase)) return "ged-badge-danger";
        if (string.Equals(status, "PENDING", StringComparison.OrdinalIgnoreCase) || string.Equals(status, "PROCESSING", StringComparison.OrdinalIgnoreCase)) return "ged-badge-warning";
        return "ged-badge-muted";
    }

    public static string AvailabilityLabel(bool isAvailable, string? status, bool hasText)
    {
        if (isAvailable) return "OCR disponível";
        if (string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase) && !hasText) return "OCR concluído sem texto";
        if (string.Equals(status, "PROCESSING", StringComparison.OrdinalIgnoreCase)) return "OCR em processamento";
        if (string.Equals(status, "PENDING", StringComparison.OrdinalIgnoreCase)) return "OCR pendente";
        if (string.Equals(status, "ERROR", StringComparison.OrdinalIgnoreCase)) return "OCR com erro";
        if (string.Equals(status, "CANCELLED", StringComparison.OrdinalIgnoreCase)) return "OCR cancelado";
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
        => string.Equals(status, "ERROR", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase);
}
