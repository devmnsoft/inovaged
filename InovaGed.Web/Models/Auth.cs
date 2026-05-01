using System.ComponentModel.DataAnnotations;

namespace InovaGed.Web.Models.Auth;

public sealed class LoginVM
{
    [Display(Name = "Tenant")]
    public string TenantSlug { get; set; } = "default";

    [Required(ErrorMessage = "Informe o e-mail ou CPF.")]
    [Display(Name = "E-mail ou CPF")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Informe a senha.")]
    [DataType(DataType.Password)]
    [Display(Name = "Senha")]
    public string Password { get; set; } = "";

    public string? ReturnUrl { get; set; }
}