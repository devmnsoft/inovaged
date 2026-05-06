namespace InovaGed.Web.Models.Protocolo;

public sealed class ProtocoloDashboardVM
{
    public int TotalEntrada { get; set; }
    public int TotalAbertos { get; set; }
    public int TotalEmTramitacao { get; set; }
    public int TotalVencidos { get; set; }
    public int TotalFinalizadosMes { get; set; }
    public int TotalArquivadosMes { get; set; }
    public decimal TempoMedioHoras { get; set; }
    public List<ProtocoloDashboardStatusVM> PorStatus { get; set; } = new();
    public List<ProtocoloDashboardSetorVM> PorSetor { get; set; } = new();
    public List<ProtocoloRelatorioRowVM> Ultimos { get; set; } = new();
}

public sealed class ProtocoloDashboardStatusVM
{
    public string Status { get; set; } = "";
    public int Total { get; set; }
}

public sealed class ProtocoloDashboardSetorVM
{
    public string Setor { get; set; } = "";
    public int Total { get; set; }
}

public sealed class ProtocoloComprovanteVM
{
    public Guid Id { get; set; }
    public string Numero { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? DataAbertura { get; set; }
    public string? Especie { get; set; }
    public string? TipoSolicitacao { get; set; }
    public string? Procedencia { get; set; }
    public string? OrigemPedido { get; set; }
    public string Assunto { get; set; } = "";
    public string? Interessado { get; set; }
    public string? CpfCnpj { get; set; }
    public string? SolicitanteNome { get; set; }
    public string? SetorOrigem { get; set; }
    public string? SetorAtual { get; set; }
    public string? CriadoPorNome { get; set; }
    public string Status { get; set; } = "";
    public DateTime? DataPrazo { get; set; }
    public int TotalAnexos { get; set; }
    public List<ProtocoloComprovanteAnexoVM> Anexos { get; set; } = new();
    public List<ProtocoloComprovanteMovimentoVM> Movimentos { get; set; } = new();
}

public sealed class ProtocoloComprovanteAnexoVM
{
    public string NomeArquivo { get; set; } = "";
    public string? SetorNome { get; set; }
    public string? AnexadoPorNome { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class ProtocoloComprovanteMovimentoVM
{
    public string Acao { get; set; } = "";
    public string? SetorOrigemNome { get; set; }
    public string? SetorDestinoNome { get; set; }
    public string? UsuarioNome { get; set; }
    public string? Justificativa { get; set; }
    public DateTime DataTramitacao { get; set; }
}

public sealed class ProtocoloRelatorioFiltroVM
{
    public string? Q { get; set; }
    public string? Status { get; set; }
    public Guid? SetorId { get; set; }
    public DateTime? De { get; set; }
    public DateTime? Ate { get; set; }
    public bool SomenteVencidos { get; set; }
    public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Setores { get; set; } = new();
    public List<ProtocoloRelatorioRowVM> Itens { get; set; } = new();
}

public sealed class ProtocoloRelatorioRowVM
{
    public Guid Id { get; set; }
    public string Numero { get; set; } = "";
    public string? Assunto { get; set; }
    public string? Interessado { get; set; }
    public string Status { get; set; } = "";
    public string? SetorAtualNome { get; set; }
    public string? SetorOrigemNome { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DataPrazo { get; set; }
    public bool Vencido { get; set; }
    public int? DiasParaVencer { get; set; }
    public int TotalAnexos { get; set; }
    public int TotalMovimentacoes { get; set; }
}

public sealed class ProtocoloNotificacaoVM
{
    public Guid Id { get; set; }
    public Guid ProtocoloId { get; set; }
    public string? Numero { get; set; }
    public string Titulo { get; set; } = "";
    public string Mensagem { get; set; } = "";
    public string Tipo { get; set; } = "";
    public bool Lida { get; set; }
    public DateTime CreatedAt { get; set; }
}
