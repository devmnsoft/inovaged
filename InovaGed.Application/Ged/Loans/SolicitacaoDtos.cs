namespace InovaGed.Application.Ged.Loans;

public enum SolicitacaoStatus
{
    PENDENTE = 0,
    DEFERIDO = 1,
    INDEFERIDO = 2,
    AGUARDAR = 3
}

public enum HistoricoSolicitacaoAcao
{
    CRIADO = 0,
    VISUALIZADO = 1,
    DEFERIDO = 2,
    INDEFERIDO = 3,
    AGUARDAR = 4
}

public sealed class SolicitacaoCreateVM
{
    public Guid? ArquivoId { get; set; }
    public string? Descricao { get; set; }
}

public sealed class SolicitacaoUpdateStatusVM
{
    public SolicitacaoStatus Status { get; set; }
    public string? Comentario { get; set; }
}

public sealed class SolicitacaoRowDto
{
    public Guid Id { get; set; }
    public Guid UsuarioId { get; set; }
    public Guid? SetorId { get; set; }
    public Guid? ArquivoId { get; set; }
    public string? ArquivoCodigo { get; set; }
    public string? ArquivoTitulo { get; set; }
    public string? UsuarioNome { get; set; }
    public string? SetorNome { get; set; }
    public string? Descricao { get; set; }
    public SolicitacaoStatus Status { get; set; }
    public string StatusLabel => Status.ToString();
    public DateTime DataSolicitacao { get; set; }
    public DateTime DataAtualizacao { get; set; }
    public Guid? AdminId { get; set; }
    public string? AdminNome { get; set; }
}

public sealed class HistoricoSolicitacaoDto
{
    public Guid Id { get; set; }
    public Guid SolicitacaoId { get; set; }
    public Guid UsuarioId { get; set; }
    public string? UsuarioNome { get; set; }
    public HistoricoSolicitacaoAcao Acao { get; set; }
    public string? Comentario { get; set; }
    public DateTime Data { get; set; }
}
