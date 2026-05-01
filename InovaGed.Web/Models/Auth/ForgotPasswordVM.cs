using System.ComponentModel.DataAnnotations;

namespace InovaGed.Web.Models.Auth;

public sealed class ForgotPasswordVM
{
    [Required(ErrorMessage = "Informe o CPF.")]
    [Display(Name = "CPF")]
    public string Cpf { get; set; } = "";

    public bool UsuarioEncontrado { get; set; }

    public Guid? UserId { get; set; }
    public Guid? TenantId { get; set; }

    public string? NomeUsuario { get; set; }
    public string? EmailUsuario { get; set; }

    [DataType(DataType.Password)]
    [MinLength(4, ErrorMessage = "A nova senha deve possuir no mínimo 4 caracteres.")]
    [Display(Name = "Nova senha")]
    public string? NovaSenha { get; set; }

    [DataType(DataType.Password)]
    [Compare(nameof(NovaSenha), ErrorMessage = "A confirmação da senha não confere.")]
    [Display(Name = "Confirmar nova senha")]
    public string? ConfirmarNovaSenha { get; set; }

    /*
        Deixe o tenant default oculto.
        Troque "default" pelo code real da tabela ged.tenant.
    */
    public string TenantSlug { get; set; } = "default";
}