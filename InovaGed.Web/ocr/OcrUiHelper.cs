namespace InovaGed.Web.Ocr;

public static class OcrUiHelper
{
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
