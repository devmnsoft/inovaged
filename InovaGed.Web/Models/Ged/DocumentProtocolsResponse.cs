namespace InovaGed.Web.Models.Ged;

public sealed class DocumentProtocolsResponse
{
    public bool Success { get; init; }
    public string Outcome { get; init; } = "unavailable";
    public string? Message { get; init; }
    public string? CorrelationId { get; init; }
    public bool CanRetry { get; init; }
    public bool CanCreate { get; init; }
    public string? CreateUrl { get; init; }
    public IReadOnlyList<DocumentProtocolLinkDto> Items { get; init; } = Array.Empty<DocumentProtocolLinkDto>();
}

public sealed class DocumentProtocolLinkDto
{
    public Guid ProtocoloId { get; init; }
    public string ProtocoloNumero { get; init; } = "";
    public string TipoVinculo { get; init; } = "";
    public string? Observacao { get; init; }
    public string? CriadoPorNome { get; init; }
    public string? DetailsUrl { get; init; }
}

public static class DocumentProtocolLinkPresenter
{
    public static DocumentProtocolLinkDto? Present(Guid protocoloId, string? numero, string? tipoVinculo, string? observacao, string? autor, bool canView, string? detailsUrl)
    {
        if (!canView || protocoloId == Guid.Empty) return null;
        return new DocumentProtocolLinkDto
        {
            ProtocoloId = protocoloId,
            ProtocoloNumero = string.IsNullOrWhiteSpace(numero) ? "Protocolo" : numero.Trim(),
            TipoVinculo = string.IsNullOrWhiteSpace(tipoVinculo) ? "DOCUMENTO_GERAL" : tipoVinculo.Trim(),
            Observacao = string.IsNullOrWhiteSpace(observacao) ? null : observacao.Trim(),
            CriadoPorNome = string.IsNullOrWhiteSpace(autor) ? null : autor.Trim(),
            DetailsUrl = string.IsNullOrWhiteSpace(detailsUrl) ? null : detailsUrl
        };
    }
}
