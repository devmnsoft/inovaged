namespace InovaGed.Web.Models.Labels;

public sealed record LabelsDemoCard(string Name, string Code, string Status, string PreviewUrl, string SampleUrl, string Accent);
public sealed record LabelsDemoViewModel(IReadOnlyList<LabelsDemoCard> Cards);
public sealed record LabelsDemoSamplesViewModel(LocDeskLabelInputModel Standard, LocDeskLabelInputModel Hol, string QrSvg);

/// <summary>Creates in-memory, fictional label data exclusively for previews; it never writes to storage.</summary>
public static class LabelsDemoData
{
    public static LocDeskLabelInputModel Standard(string kind = LocDeskLabelKind.Folder) => new()
    {
        TemplateCode = kind == LocDeskLabelKind.Box ? LabelTemplateCode.LocDeskBox : LabelTemplateCode.LocDeskFolder, LabelKind = kind,
        ArchiveTitle = "ARQUIVO LOCDESCK ANANINDEUA", ControlNumber = "0001", VolumeNumber = 1, VolumeTotal = 3,
        Subject = "Fiscalização PJ - DPF's e documentos avulsos ref. RFF-F", Details = "0", Activity = "FIM",
        Classification = "321.2 - PESSOAS JURÍDICAS", Support = "1. Papel", DocumentPeriod = "até 2004",
        CurrentPhase = "4. Eliminação", EliminationForecast = "2025", EliminationStatus = "2. LED pendente de elaboração",
        LedNumber = "N/A", Location = "LOC.AN.101.E1.P1"
    };

    public static LocDeskLabelInputModel Hol() => new()
    {
        TemplateCode = LabelTemplateCode.LocDeskFolderHol, LabelKind = LocDeskLabelKind.Folder,
        ArchiveTitle = "ARQUIVO LOCDESCK ANANINDEUA", Contract = "Hosp. Ophir Loyola", ControlNumber = "199",
        Subject = "PRONTUÁRIO nº: 100.334", Details = "DAME - ALTA MEDICA", Activity = "FIM",
        Classification = "HOL.132.3 - LAUDO DE PROCEDIMENTOS DIAGNÓSTICOS", Support = "1. PAPEL",
        PeriodStart = new DateTime(2017, 7, 15), PeriodEnd = new DateTime(2017, 9, 25),
        CurrentPhase = "2. GUARDA INTERMEDIÁRIA", EliminationForecast = "0. GUARDA PERMANENTE",
        EliminationStatus = "0. GUARDA PERMANENTE", LedNumber = "N/A", Location = "LOC.AN.___.E___.P___"
    };
}
