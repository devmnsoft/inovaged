using System.ComponentModel.DataAnnotations;

namespace InovaGed.Web.Models.Ged;

public sealed class RenameFolderVM
{
    [Required]
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Informe o novo nome.")]
    [StringLength(200, ErrorMessage = "O nome deve ter no máximo 200 caracteres.")]
    public string? Nome { get; set; }
}
