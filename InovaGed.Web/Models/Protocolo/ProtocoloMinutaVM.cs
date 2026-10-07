namespace InovaGed.Web.Models.Protocolo;

public sealed class ProtocoloMinutaVM
{
    public Guid Id { get; set; }
    public Guid SetorId { get; set; }
    public string SetorNome { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string Conteudo { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Versao { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string AtualizadoPorNome { get; set; } = string.Empty;
    public bool PodeEditar { get; set; }
    public bool PodeConfirmar { get; set; }
    public bool PodeEncaminhar { get; set; }
    public bool PodeDescartar { get; set; }
}

public sealed class ProtocoloMinutaHistoricoVM
{
    public Guid MinutaId { get; set; }
    public int Versao { get; set; }
    public string Evento { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string UsuarioNome { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
