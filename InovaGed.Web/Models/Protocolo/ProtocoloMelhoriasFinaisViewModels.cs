using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace InovaGed.Web.Models.Protocolo;

public sealed class ProtocoloParametroVM
{
    public Guid Id { get; set; }
    public string Chave { get; set; } = "";
    public string? Valor { get; set; }
    public string? Descricao { get; set; }
    public string Tipo { get; set; } = "TEXT";
    public string Grupo { get; set; } = "GERAL";
    public bool Ativo { get; set; } = true;
}

public sealed class ProtocoloParametrosIndexVM
{
    public List<ProtocoloParametroVM> Itens { get; set; } = new();
}

public sealed class ProtocoloEditarVM
{
    public Guid Id { get; set; }
    public string Numero { get; set; } = "";
    public string Status { get; set; } = "";

    [Required(ErrorMessage = "Informe o assunto.")]
    [StringLength(300)]
    public string Assunto { get; set; } = "";

    [StringLength(150)]
    public string? Especie { get; set; }

    [StringLength(250)]
    public string? TipoSolicitacao { get; set; }

    [StringLength(100)]
    public string? Procedencia { get; set; }

    [StringLength(150)]
    public string? OrigemPedido { get; set; }

    public string? Descricao { get; set; }
    public string? InformacoesComplementares { get; set; }

    [StringLength(250)]
    public string? Interessado { get; set; }

    [StringLength(30)]
    public string? CpfCnpj { get; set; }

    [StringLength(200)]
    public string? Email { get; set; }

    [StringLength(50)]
    public string? Telefone { get; set; }

    [StringLength(250)]
    public string? SolicitanteNome { get; set; }

    [StringLength(80)]
    public string? SolicitanteMatricula { get; set; }

    [StringLength(200)]
    public string? SolicitanteCargo { get; set; }

    public DateTime? DataPrazo { get; set; }

    [Required(ErrorMessage = "Informe a justificativa da alteração.")]
    public string JustificativaEdicao { get; set; } = "";
}

public sealed class ProtocoloGedVinculoVM
{
    public Guid Id { get; set; }
    public Guid ProtocoloId { get; set; }
    public string? ProtocoloNumero { get; set; }
    public Guid? ProtocoloDocumentoId { get; set; }
    public string? ProtocoloAnexoNome { get; set; }
    public Guid? GedDocumentId { get; set; }
    public string TipoVinculo { get; set; } = "";
    public string? Observacao { get; set; }
    public string? CriadoPorNome { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class ProtocoloGedVincularVM
{
    public Guid ProtocoloId { get; set; }
    public string? ProtocoloNumero { get; set; }
    public Guid? ProtocoloDocumentoId { get; set; }

    [Required(ErrorMessage = "Informe o ID do documento GED.")]
    public Guid GedDocumentId { get; set; }

    public string TipoVinculo { get; set; } = "VINCULO";
    public string? Observacao { get; set; }
    public List<SelectListItem> Anexos { get; set; } = new();
    public List<ProtocoloGedVinculoVM> Vinculos { get; set; } = new();
}

public sealed class ProtocoloUsuarioBuscaVM
{
    public string? Q { get; set; }
    public List<ProtocoloUsuarioBuscaItemVM> Usuarios { get; set; } = new();
}

public sealed class ProtocoloUsuarioBuscaItemVM
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = "";
    public string? Email { get; set; }
}

public sealed class ProtocoloUsuarioSetorPermissaoVM
{
    public Guid? Id { get; set; }
    [Required] public Guid UsuarioId { get; set; }
    public string? UsuarioNome { get; set; }
    [Required] public Guid SetorId { get; set; }
    public bool PodeVisualizar { get; set; } = true;
    public bool PodeReceber { get; set; } = true;
    public bool PodeTramitar { get; set; } = true;
    public bool PodeAnexar { get; set; } = true;
    public bool PodeExcluirAnexo { get; set; } = true;
    public bool PodeDecidir { get; set; }
    public bool PodeArquivar { get; set; }
    public bool Ativo { get; set; } = true;
    public List<SelectListItem> Setores { get; set; } = new();
}

public sealed class ProtocoloValidacaoVM
{
    public bool Encontrado { get; set; }
    public string? Numero { get; set; }
    public string? Assunto { get; set; }
    public string? Interessado { get; set; }
    public string? Status { get; set; }
    public DateTime? CreatedAt { get; set; }
    public string? HashComprovante { get; set; }
}
