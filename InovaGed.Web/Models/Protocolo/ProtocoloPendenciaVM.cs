namespace InovaGed.Web.Models.Protocolo;

public sealed class ProtocoloPendenciaVM
{
    public Guid Id { get; set; }
    public Guid SetorId { get; set; }
    public string Descricao { get; set; } = string.Empty;
    public string? Evidencia { get; set; }
    public string? FonteEvidencia { get; set; }
    public string Origem { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? AtribuidaPara { get; set; }
    public string? ResponsavelNome { get; set; }
    public string? Resolucao { get; set; }
    public Guid? ComprovanteDocumentoId { get; set; }
    public string? ComprovanteNome { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool PodeAtribuir { get; set; }
    public bool PodeResolver { get; set; }
    public bool PodeDescartar { get; set; }
}

public sealed class ProtocoloPendenciaHistoricoVM
{
    public Guid PendenciaId { get; set; }
    public string Evento { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string UsuarioNome { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
