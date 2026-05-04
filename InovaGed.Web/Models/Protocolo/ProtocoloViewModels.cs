using Microsoft.AspNetCore.Mvc.Rendering;

namespace InovaGed.Web.Models.Protocolo;

public sealed class ProtocoloIndexVM
{
    public string? Q { get; set; }
    public string? Status { get; set; }
    public IReadOnlyList<ProtocoloGridItemVM> Protocolos { get; set; } = Array.Empty<ProtocoloGridItemVM>();
}

public sealed class ProtocoloGridItemVM
{
    public Guid Id { get; set; }
    public string Numero { get; set; } = "";
    public DateTime Emissao { get; set; }
    public string Lotacao { get; set; } = "";
    public string TipoSolicitacao { get; set; } = "";
    public string Interessado { get; set; } = "";
    public string Assunto { get; set; } = "";
    public string Status { get; set; } = "";
}

public sealed class ProtocoloFormVM
{
    public Guid? TipoId { get; set; }
    public Guid? AssuntoId { get; set; }
    public Guid? PrioridadeId { get; set; }
    public Guid? CanalEntradaId { get; set; }

    public Guid SetorOrigemId { get; set; }
    public Guid SetorDestinoId { get; set; }

    public string Assunto { get; set; } = "";
    public string? Especie { get; set; }
    public string? Procedencia { get; set; }

    public string? Interessado { get; set; }
    public string? CpfCnpj { get; set; }
    public string? Email { get; set; }
    public string? Telefone { get; set; }

    public string? Descricao { get; set; }
    public string? InformacoesComplementares { get; set; }
    public string? ObservacaoInicial { get; set; }

    public bool SalvarComoRascunho { get; set; }

    public List<SelectListItem> Tipos { get; set; } = new();
    public List<SelectListItem> Assuntos { get; set; } = new();
    public List<SelectListItem> Prioridades { get; set; } = new();
    public List<SelectListItem> CanaisEntrada { get; set; } = new();
    public List<SelectListItem> Setores { get; set; } = new();
    public List<SelectListItem> TiposDocumento { get; set; } = new();
}

public sealed class ProtocoloDetailsVM
{
    public Guid Id { get; set; }
    public string Numero { get; set; } = "";
    public DateTime Emissao { get; set; }
    public string Status { get; set; } = "";

    public string TipoSolicitacao { get; set; } = "";
    public string Prioridade { get; set; } = "";
    public string CanalEntrada { get; set; } = "";

    public string Assunto { get; set; } = "";
    public string? Especie { get; set; }
    public string? Procedencia { get; set; }

    public string? Interessado { get; set; }
    public string? CpfCnpj { get; set; }
    public string? Email { get; set; }
    public string? Telefone { get; set; }

    public string? Descricao { get; set; }
    public string? InformacoesComplementares { get; set; }
    public string? JustificativaEncerramento { get; set; }

    public string SetorOrigem { get; set; } = "";
    public string SetorAtual { get; set; } = "";
    public Guid? SetorAtualId { get; set; }

    public bool PodeAtuar { get; set; }
    public bool PodeAnexar { get; set; }
    public bool PodeTramitar { get; set; }
    public bool PodeFinalizar { get; set; }
    public bool PodeArquivar { get; set; }

    public IReadOnlyList<ProtocoloDocumentoVM> Documentos { get; set; } = Array.Empty<ProtocoloDocumentoVM>();
    public IReadOnlyList<ProtocoloTramitacaoVM> Tramitacoes { get; set; } = Array.Empty<ProtocoloTramitacaoVM>();
    public IReadOnlyList<ProtocoloObservacaoVM> Observacoes { get; set; } = Array.Empty<ProtocoloObservacaoVM>();

    public List<SelectListItem> SetoresDestino { get; set; } = new();
    public List<SelectListItem> TiposDocumento { get; set; } = new();
}

public sealed class ProtocoloDocumentoVM
{
    public Guid Id { get; set; }
    public string NomeArquivo { get; set; } = "";
    public string? ContentType { get; set; }
    public long? TamanhoBytes { get; set; }
    public string? TipoDocumento { get; set; }
    public string? Descricao { get; set; }
    public string Setor { get; set; } = "";
    public Guid? SetorId { get; set; }
    public string AnexadoPor { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public bool PodeExcluir { get; set; }
}

public sealed class ProtocoloTramitacaoVM
{
    public Guid Id { get; set; }
    public string Acao { get; set; } = "";
    public string? StatusAnterior { get; set; }
    public string? StatusNovo { get; set; }
    public string? SetorOrigem { get; set; }
    public string? SetorDestino { get; set; }
    public string? UsuarioNome { get; set; }
    public string? Despacho { get; set; }
    public string? Observacao { get; set; }
    public string? Justificativa { get; set; }
    public DateTime DataTramitacao { get; set; }
}

public sealed class ProtocoloObservacaoVM
{
    public Guid Id { get; set; }
    public string Tipo { get; set; } = "";
    public string Observacao { get; set; } = "";
    public string Setor { get; set; } = "";
    public string UsuarioNome { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public sealed class ProtocoloLookupRow
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = "";
}
