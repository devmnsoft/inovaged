using System.ComponentModel.DataAnnotations;

namespace InovaGed.Web.Models.Labels;

public sealed class LocDeskLabelInputModel : IValidatableObject
{
    public string LabelKind { get; set; } = LocDeskLabelKind.Folder;
    [Required, StringLength(160)] public string ArchiveTitle { get; set; } = "ARQUIVO LOCDESCK ANANINDEUA";
    [StringLength(100)] public string? ProcessNumber { get; set; }
    [Required, StringLength(50)] public string ControlNumber { get; set; } = "0001";
    [Range(1, int.MaxValue)] public int VolumeNumber { get; set; } = 1;
    [Range(1, int.MaxValue)] public int VolumeTotal { get; set; } = 3;
    [StringLength(1000)] public string Subject { get; set; } = "Fiscalização PJ - DPF's e documentos avulsos ref. RFF-F";
    [StringLength(500)] public string? Details { get; set; } = "0";
    [StringLength(100)] public string Activity { get; set; } = "FIM";
    [StringLength(500)] public string Classification { get; set; } = "321.2 - PESSOAS JURÍDICAS";
    [StringLength(100)] public string Support { get; set; } = "1. Papel";
    [StringLength(200)] public string DocumentPeriod { get; set; } = "até 2004";
    [StringLength(200)] public string CurrentPhase { get; set; } = "4. Eliminação";
    [StringLength(100)] public string EliminationForecast { get; set; } = "2025";
    [StringLength(300)] public string EliminationStatus { get; set; } = "2. LED pendente de elaboração";
    [StringLength(100)] public string LedNumber { get; set; } = "N/A";
    [StringLength(300)] public string Location { get; set; } = "LOC.AN.101.E1.P1";
    public Guid? BoxId { get; set; }
    public Guid? DocumentId { get; set; }
    [StringLength(500)] public string? ReprintReason { get; set; }
    [Range(1, 500)] public int Copies { get; set; } = 1;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!LocDeskLabelKind.IsValid(LabelKind)) yield return new("Tipo de etiqueta inválido.", [nameof(LabelKind)]);
        if (VolumeTotal < VolumeNumber) yield return new("O total de volumes deve ser igual ou maior que o volume atual.", [nameof(VolumeTotal)]);
        if (LabelKind == LocDeskLabelKind.Folder && string.IsNullOrWhiteSpace(Subject)) yield return new("Assunto é obrigatório para etiqueta de pasta.", [nameof(Subject)]);
        if (LabelKind == LocDeskLabelKind.Box && string.IsNullOrWhiteSpace(Location)) yield return new("Localização é obrigatória para etiqueta de caixa.", [nameof(Location)]);
    }
}
