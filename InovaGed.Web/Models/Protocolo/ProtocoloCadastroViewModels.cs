using System.ComponentModel.DataAnnotations;

namespace InovaGed.Web.Models.Protocolo;

public sealed class ProtocoloCadastroIndexVM
{
    public string Tipo { get; set; } = "setores";
    public string Titulo { get; set; } = "Setores";
    public string? Q { get; set; }
    public List<ProtocoloCadastroRowVM> Itens { get; set; } = new();
}

public sealed class ProtocoloCadastroRowVM
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string? Codigo { get; set; }
    public string? Sigla { get; set; }
    public string? Descricao { get; set; }
    public bool Ativo { get; set; }
    public int? Ordem { get; set; }
    public int? PrazoDias { get; set; }
    public string? Cor { get; set; }
    public bool? Obrigatorio { get; set; }
    public bool? PermiteMultiplos { get; set; }
    public bool? ExigeInteressado { get; set; }
    public bool? ExigeDocumentoInicial { get; set; }
}

public sealed class ProtocoloCadastroFormVM
{
    public Guid? Id { get; set; }

    [Required]
    public string Tipo { get; set; } = "setores";

    [Required(ErrorMessage = "Informe o nome.")]
    [StringLength(250)]
    public string Nome { get; set; } = string.Empty;

    [StringLength(50)]
    public string? Codigo { get; set; }

    [StringLength(30)]
    public string? Sigla { get; set; }

    public string? Descricao { get; set; }

    public bool Ativo { get; set; } = true;

    public int? Ordem { get; set; }

    [Display(Name = "Prazo em dias")]
    public int? PrazoDias { get; set; }

    [StringLength(30)]
    public string? Cor { get; set; }

    public bool Obrigatorio { get; set; }

    [Display(Name = "Permite múltiplos documentos")]
    public bool PermiteMultiplos { get; set; } = true;

    [Display(Name = "Exige interessado")]
    public bool ExigeInteressado { get; set; } = true;

    [Display(Name = "Exige documento inicial")]
    public bool ExigeDocumentoInicial { get; set; }

    public Guid? TipoProtocoloId { get; set; }
    public Guid? SetorPadraoId { get; set; }

    public List<ProtocoloSelectItemVM> TiposProtocolo { get; set; } = new();
    public List<ProtocoloSelectItemVM> Setores { get; set; } = new();
}

public sealed class ProtocoloSelectItemVM
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = string.Empty;
}
