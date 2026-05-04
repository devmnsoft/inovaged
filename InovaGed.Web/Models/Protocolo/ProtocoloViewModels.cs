using System.ComponentModel.DataAnnotations;

namespace InovaGed.Web.Models.Protocolo;

public sealed class ProtocoloIndexVM
{
    public string? Q { get; set; }
    public string? Status { get; set; }
    public Guid? SetorId { get; set; }
    public List<ProtocoloSetorOptionVM> Setores { get; set; } = new();
    public List<ProtocoloListItemVM> Protocolos { get; set; } = new();
}

public sealed class ProtocoloListItemVM
{
    public Guid Id { get; set; }
    public string Numero { get; set; } = string.Empty;
    public string? TipoProtocolo { get; set; }
    public string Assunto { get; set; } = string.Empty;
    public string? Interessado { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Prioridade { get; set; }
    public string? SetorAtual { get; set; }
    public string? SetorOrigem { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DataAbertura { get; set; }
}

public sealed class ProtocoloFormVM
{
    public Guid? Id { get; set; }

    [Display(Name = "Tipo da solicitação")]
    public Guid? TipoId { get; set; }

    [Display(Name = "Assunto cadastrado")]
    public Guid? AssuntoId { get; set; }

    [Display(Name = "Canal de entrada")]
    public Guid? CanalEntradaId { get; set; }

    [Display(Name = "Prioridade")]
    public Guid? PrioridadeId { get; set; }

    [Required(ErrorMessage = "Informe o assunto.")]
    [StringLength(300)]
    public string Assunto { get; set; } = string.Empty;

    [StringLength(250)]
    public string? Interessado { get; set; }

    [Display(Name = "CPF/CNPJ")]
    [StringLength(30)]
    public string? CpfCnpj { get; set; }

    [EmailAddress(ErrorMessage = "E-mail inválido.")]
    [StringLength(200)]
    public string? Email { get; set; }

    [StringLength(50)]
    public string? Telefone { get; set; }

    [Display(Name = "Informações complementares")]
    public string? Descricao { get; set; }

    [Required(ErrorMessage = "Informe o setor de origem.")]
    [Display(Name = "Setor de origem")]
    public Guid SetorOrigemId { get; set; }

    [Display(Name = "Setor para tramitação inicial")]
    public Guid? SetorDestinoInicialId { get; set; }

    public bool SalvarComoRascunho { get; set; } = false;

    public List<ProtocoloSetorOptionVM> Setores { get; set; } = new();
    public List<ProtocoloOptionVM> Tipos { get; set; } = new();
    public List<ProtocoloOptionVM> Assuntos { get; set; } = new();
    public List<ProtocoloOptionVM> CanaisEntrada { get; set; } = new();
    public List<ProtocoloOptionVM> Prioridades { get; set; } = new();
    public List<ProtocoloTipoDocumentoOptionVM> TiposDocumento { get; set; } = new();
}

public sealed class ProtocoloDetailsVM
{
    public Guid Id { get; set; }
    public string Numero { get; set; } = string.Empty;
    public string Assunto { get; set; } = string.Empty;
    public string? TipoProtocolo { get; set; }
    public string? CanalEntrada { get; set; }
    public string? Prioridade { get; set; }
    public string? Interessado { get; set; }
    public string? CpfCnpj { get; set; }
    public string? Email { get; set; }
    public string? Telefone { get; set; }
    public string? Descricao { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid? SetorAtualId { get; set; }
    public string? SetorAtual { get; set; }
    public Guid SetorOrigemId { get; set; }
    public string? SetorOrigem { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DataAbertura { get; set; }
    public DateTime? DataEncerramento { get; set; }

    public bool PodeVisualizar { get; set; }
    public bool PodeEditar { get; set; }
    public bool PodeAnexar { get; set; }
    public bool PodeTramitar { get; set; }
    public bool PodeDeferir { get; set; }
    public bool PodeIndeferir { get; set; }
    public bool PodeArquivar { get; set; }

    public List<ProtocoloSetorOptionVM> SetoresDestino { get; set; } = new();
    public List<ProtocoloTipoDocumentoOptionVM> TiposDocumento { get; set; } = new();
    public List<ProtocoloDocumentoVM> Documentos { get; set; } = new();
    public List<ProtocoloTramitacaoVM> Tramitacoes { get; set; } = new();
    public List<ProtocoloObservacaoVM> Observacoes { get; set; } = new();
}

public sealed class ProtocoloDocumentoVM
{
    public Guid Id { get; set; }
    public string NomeArquivo { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long? TamanhoBytes { get; set; }
    public string? TipoDocumento { get; set; }
    public string? Descricao { get; set; }
    public string? AnexadoPorNome { get; set; }
    public string? Setor { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class ProtocoloTramitacaoVM
{
    public Guid Id { get; set; }
    public string Acao { get; set; } = string.Empty;
    public string? StatusAnterior { get; set; }
    public string? StatusNovo { get; set; }
    public string? SetorOrigem { get; set; }
    public string? SetorDestino { get; set; }
    public string? UsuarioNome { get; set; }
    public string? Despacho { get; set; }
    public string? Observacao { get; set; }
    public DateTime DataTramitacao { get; set; }
}

public sealed class ProtocoloObservacaoVM
{
    public Guid Id { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public string Observacao { get; set; } = string.Empty;
    public string? Setor { get; set; }
    public string? UsuarioNome { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class ProtocoloSetorOptionVM
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string? Sigla { get; set; }
}

public sealed class ProtocoloOptionVM
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string? Codigo { get; set; }
}

public sealed class ProtocoloTipoDocumentoOptionVM
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public bool Obrigatorio { get; set; }
}

public sealed class ProtocoloSetorFormVM
{
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "Informe o nome do setor.")]
    [StringLength(200)]
    public string Nome { get; set; } = string.Empty;

    [StringLength(30)]
    public string? Sigla { get; set; }

    public string? Descricao { get; set; }
    public bool Ativo { get; set; } = true;
}

public sealed class ProtocoloSetoresIndexVM
{
    public List<ProtocoloSetorFormVM> Setores { get; set; } = new();
}
