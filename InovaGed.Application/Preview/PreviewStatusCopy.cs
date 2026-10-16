namespace InovaGed.Application.Preview;

public sealed record PreviewStatusCopy(string Code, string Headline, string Detail, bool Terminal, bool Retryable)
{
    public static PreviewStatusCopy Describe(string? status)
    {
        var code = (status ?? string.Empty).Trim().ToUpperInvariant();
        return code switch
        {
            "READY" or "COMPLETED" => new("READY", "Disponível", "A visualização está pronta.", true, false),
            "PROCESSING" => new("PROCESSING", "Gerando visualização", "O documento foi recebido. Estamos preparando a visualização. Você pode continuar trabalhando enquanto o processamento acontece.", false, false),
            "FAILED" or "ERROR" => new("FAILED", "Falha recuperável", "Não foi possível gerar a visualização deste arquivo. O documento original permanece armazenado com segurança.", true, true),
            "CANCELED" or "CANCELLED" => new("CANCELED", "Cancelado", "A preparação da visualização foi cancelada. O documento original permanece armazenado.", true, true),
            "UNSUPPORTED" => new("UNSUPPORTED", "Não suportado", "Este formato não pode ser exibido aqui. O documento original permanece armazenado e pode ser baixado, quando autorizado.", true, false),
            "NOT_READY" or "PENDING" or "" => new(code == "" ? "PENDING" : code, "Aguardando", "O documento foi recebido. Estamos preparando a visualização. Você pode continuar trabalhando enquanto o processamento acontece.", false, false),
            _ => new(code, "Aguardando", "O documento foi recebido. Estamos preparando a visualização. Você pode continuar trabalhando enquanto o processamento acontece.", false, false)
        };
    }
}
