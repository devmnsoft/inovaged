using System.ComponentModel.DataAnnotations;

namespace InovaGed.Web.Models.Users;

public sealed class CreateUserVM
{
    public Guid? ServidorId { get; set; }

    [Required(ErrorMessage = "Informe o nome completo.")]
    [StringLength(200, MinimumLength = 3)]
    [Display(Name = "Nome completo")]
    public string NomeCompleto { get; set; } = "";

    [Required(ErrorMessage = "Informe o CPF.")]
    [StringLength(14)]
    [Display(Name = "CPF")]
    public string Cpf { get; set; } = "";

    [StringLength(30)]
    [Display(Name = "RG")]
    public string? Rg { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Data de nascimento")]
    public DateTime? DataNascimento { get; set; }

    [EmailAddress]
    [StringLength(255)]
    [Display(Name = "E-mail institucional")]
    public string? EmailInstitucional { get; set; }

    [EmailAddress]
    [StringLength(255)]
    [Display(Name = "E-mail alternativo")]
    public string? EmailAlternativo { get; set; }

    [StringLength(30)]
    public string? Telefone { get; set; }

    [StringLength(30)]
    public string? Celular { get; set; }

    [StringLength(50)]
    [Display(Name = "Matrícula")]
    public string? Matricula { get; set; }

    [StringLength(120)]
    public string? Cargo { get; set; }

    [StringLength(120)]
    [Display(Name = "Função")]
    public string? Funcao { get; set; }

    [StringLength(150)]
    public string? Setor { get; set; }

    [StringLength(150)]
    [Display(Name = "Lotação")]
    public string? Lotacao { get; set; }

    [StringLength(150)]
    public string? Unidade { get; set; }

    [StringLength(80)]
    [Display(Name = "Tipo de vínculo")]
    public string? TipoVinculo { get; set; }

    [StringLength(30)]
    [Display(Name = "Conselho profissional")]
    public string? ConselhoProfissional { get; set; }

    [StringLength(50)]
    [Display(Name = "Número do conselho")]
    public string? NumeroConselho { get; set; }

    [StringLength(2)]
    [Display(Name = "UF do conselho")]
    public string? UfConselho { get; set; }

    [StringLength(120)]
    public string? Especialidade { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Data de admissão")]
    public DateTime? DataAdmissao { get; set; }

    [Required]
    [Display(Name = "Situação funcional")]
    public string SituacaoFuncional { get; set; } = "ATIVO";

    [StringLength(2000)]
    [Display(Name = "Observações")]
    public string? Observacao { get; set; }

    [Display(Name = "Criar usuário de acesso ao sistema")]
    public bool CriarUsuarioAcesso { get; set; } = true;

    [EmailAddress]
    [StringLength(255)]
    [Display(Name = "E-mail de login")]
    public string EmailLogin { get; set; } = "";

    [StringLength(120)]
    [Display(Name = "Nome de usuário")]
    public string? UserName { get; set; }

    [MinLength(8, ErrorMessage = "A senha deve possuir pelo menos 8 caracteres.")]
    [DataType(DataType.Password)]
    [Display(Name = "Senha inicial")]
    public string Password { get; set; } = "";

    [Compare(nameof(Password), ErrorMessage = "A confirmação não confere com a senha.")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirmar senha")]
    public string ConfirmPassword { get; set; } = "";

    [Display(Name = "Usuário ativo")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "Obrigar troca de senha no primeiro acesso")]
    public bool MustChangePassword { get; set; } = true;

    [Display(Name = "Exigir MFA")]
    public bool MfaEnabled { get; set; }

    [Display(Name = "Exigir certificado digital")]
    public bool CertificateRequired { get; set; }

    [Display(Name = "Pode assinar com ICP-Brasil")]
    public bool CanSignWithIcp { get; set; }

    [Display(Name = "Nível de sigilo máximo")]
    public string SecurityLevel { get; set; } = "PUBLIC";

    public List<Guid> SelectedRoleIds { get; set; } = new();

    public List<RoleItem> AvailableRoles { get; set; } = new();

    public List<SelectItem> SituacoesFuncionais { get; set; } = new()
    {
        new("ATIVO", "Ativo"),
        new("INATIVO", "Inativo"),
        new("AFASTADO", "Afastado"),
        new("LICENCA", "Licença"),
        new("EXONERADO", "Exonerado"),
        new("DESLIGADO", "Desligado")
    };

    public List<SelectItem> SecurityLevels { get; set; } = new()
    {
        new("PUBLIC", "Público"),
        new("RESTRICTED", "Restrito"),
        new("CONFIDENTIAL", "Confidencial")
    };

    public sealed class RoleItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
    }

    public sealed class SelectItem
    {
        public SelectItem(string value, string text)
        {
            Value = value;
            Text = text;
        }

        public string Value { get; set; }
        public string Text { get; set; }
    }
}