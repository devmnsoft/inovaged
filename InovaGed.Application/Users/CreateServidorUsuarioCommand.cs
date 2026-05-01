namespace InovaGed.Application.Users;

public sealed class CreateServidorUsuarioCommand
{
    public Guid? ServidorId { get; set; }

    public string NomeCompleto { get; set; } = "";
    public string Cpf { get; set; } = "";
    public string? Rg { get; set; }
    public DateTime? DataNascimento { get; set; }

    public string? EmailInstitucional { get; set; }
    public string? EmailAlternativo { get; set; }
    public string? Telefone { get; set; }
    public string? Celular { get; set; }

    public string? Matricula { get; set; }
    public string? Cargo { get; set; }
    public string? Funcao { get; set; }
    public string? Setor { get; set; }
    public string? Lotacao { get; set; }
    public string? Unidade { get; set; }
    public string? TipoVinculo { get; set; }

    public string? ConselhoProfissional { get; set; }
    public string? NumeroConselho { get; set; }
    public string? UfConselho { get; set; }
    public string? Especialidade { get; set; }

    public DateTime? DataAdmissao { get; set; }
    public string SituacaoFuncional { get; set; } = "ATIVO";
    public string? Observacao { get; set; }

    public bool CriarUsuarioAcesso { get; set; } = true;

    public string EmailLogin { get; set; } = "";
    public string? UserName { get; set; }
    public string PasswordHash { get; set; } = "";

    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; } = true;
    public bool MfaEnabled { get; set; }
    public bool CertificateRequired { get; set; }
    public bool CanSignWithIcp { get; set; }

    public string SecurityLevel { get; set; } = "PUBLIC";

    public IReadOnlyList<Guid> RoleIds { get; set; } = Array.Empty<Guid>();

    public Guid? CreatedBy { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? CorrelationId { get; set; }
}