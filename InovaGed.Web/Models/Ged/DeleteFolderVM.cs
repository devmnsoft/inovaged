using System.ComponentModel.DataAnnotations;

namespace InovaGed.Web.Models.Ged;

public sealed class DeleteFolderVM
{
    [Required]
    public Guid Id { get; set; }
}
