using System.ComponentModel.DataAnnotations;

namespace InovaGed.Web.Models.Ged;

public sealed class UploadDocumentoVM
{
    [Required(ErrorMessage = "Selecione um arquivo.")]
    public IFormFile? Arquivo { get; set; }

    [Required(ErrorMessage = "Informe o título.")]
    [StringLength(200, ErrorMessage = "O título deve ter no máximo 200 caracteres.")]
    public string? Titulo { get; set; }

    [StringLength(4000, ErrorMessage = "A descrição deve ter no máximo 4000 caracteres.")]
    public string? Descricao { get; set; }

    public Guid? FolderId { get; set; }

    public Guid? TypeId { get; set; }

    public bool Confidencial { get; set; }
}
