using System.ComponentModel.DataAnnotations;
namespace InovaGed.Web.Models.Labels;
public sealed class LocDeskLabelBatchPrintModel
{
    [Required, MinLength(1)] public List<LocDeskLabelInputModel> Labels { get; set; } = [];
    [StringLength(500)] public string? ReprintReason { get; set; }
}
