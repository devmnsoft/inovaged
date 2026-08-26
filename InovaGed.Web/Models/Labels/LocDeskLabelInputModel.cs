using System.ComponentModel.DataAnnotations;

namespace InovaGed.Web.Models.Labels;

public sealed class LocDeskLabelInputModel : IValidatableObject
{
    public string TemplateCode { get; set; } = LabelTemplateCode.LocDeskFolder;
    public string LabelKind { get; set; } = LocDeskLabelKind.Folder;
    [Required, StringLength(160)] public string ArchiveTitle { get; set; } = "ARQUIVO LOCDESCK ANANINDEUA";
    [StringLength(100)] public string? ProcessNumber { get; set; }
    [StringLength(160)] public string Contract { get; set; } = "Hosp. Ophir Loyola";
    [StringLength(100)] public string? MedicalRecordNumber { get; set; }
    [DataType(DataType.Date)] public DateTime? PeriodStart { get; set; }
    [DataType(DataType.Date)] public DateTime? PeriodEnd { get; set; }
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
        if (VolumeTotal < VolumeNumber) yield return new("O volume atual não pode ser maior que o total de volumes.", [nameof(VolumeNumber), nameof(VolumeTotal)]);
        if (PeriodStart > PeriodEnd) yield return new("O período inicial não pode ser maior que o período final.", [nameof(PeriodStart), nameof(PeriodEnd)]);
        if (TemplateCode == LabelTemplateCode.LocDeskFolderHol)
        {
            if (string.IsNullOrWhiteSpace(ControlNumber)) yield return new("Informe o número de controle da etiqueta.", [nameof(ControlNumber)]);
            if (string.IsNullOrWhiteSpace(Contract)) yield return new("Informe o contrato.", [nameof(Contract)]);
            if (string.IsNullOrWhiteSpace(Subject)) yield return new("Informe o assunto do documento.", [nameof(Subject)]);
            if (string.IsNullOrWhiteSpace(Activity)) yield return new("Informe a atividade.", [nameof(Activity)]);
            if (string.IsNullOrWhiteSpace(Classification)) yield return new("Informe a classificação.", [nameof(Classification)]);
            if (string.IsNullOrWhiteSpace(Support)) yield return new("Informe o suporte.", [nameof(Support)]);
        }
        if (LabelKind == LocDeskLabelKind.Folder && string.IsNullOrWhiteSpace(Subject)) yield return new("Assunto é obrigatório para etiqueta de pasta.", [nameof(Subject)]);
        if (LabelKind == LocDeskLabelKind.Box && string.IsNullOrWhiteSpace(Location)) yield return new("Localização é obrigatória para etiqueta de caixa.", [nameof(Location)]);
    }
}
