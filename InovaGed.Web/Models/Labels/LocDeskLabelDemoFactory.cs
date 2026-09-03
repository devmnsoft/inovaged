using InovaGed.Application.Labels;
using InovaGed.Web.Models.Branding;

namespace InovaGed.Web.Models.Labels;

/// <summary>Builds complete LocDesk render models outside Razor views.</summary>
public static class LocDeskLabelDemoFactory
{
    public static LocDeskLabelRenderModel CreateBoxDemo(PrintLogoViewModel? logo = null) =>
        Create(CreateDemoInput(LabelTemplateCode.LocDeskBox), "", false, null, logo);

    public static LocDeskLabelRenderModel CreateFolderDemo(PrintLogoViewModel? logo = null) =>
        Create(CreateDemoInput(LabelTemplateCode.LocDeskFolder), "", false, null, logo);

    public static LocDeskLabelRenderModel CreateHolDemo(PrintLogoViewModel? logo = null) =>
        Create(CreateDemoInput(LabelTemplateCode.LocDeskFolderHol), "", false, null, logo);

    public static LocDeskLabelRenderModel Create(
        LocDeskLabelInputModel label, string qrSvg, bool printRegistered,
        LabelTemplateDetails? template, PrintLogoViewModel? logo = null,
        string? warning = null, IReadOnlyList<LocDeskLabelInputModel>? labels = null)
    {
        var appliedLogo = logo ?? PrintLogoViewModel.Empty;
        var sourceLabels = labels is { Count: > 0 }
            ? labels
            : Enumerable.Repeat(label, Math.Max(1, label.Copies)).ToArray();
        var children = sourceLabels.Select(item => new LocDeskLabelRenderModel
        {
            Label = item,
            QrSvg = qrSvg,
            PrintRegistered = printRegistered,
            Template = template,
            PrintLogo = appliedLogo,
            PrintLogoWarning = warning
        }).ToArray();
        return new LocDeskLabelRenderModel
        {
            Label = label,
            Labels = sourceLabels,
            QrSvg = qrSvg,
            PrintRegistered = printRegistered,
            Template = template,
            PrintLogo = appliedLogo,
            PrintLogoWarning = warning,
            RenderLabels = children
        };
    }

    private static LocDeskLabelInputModel CreateDemoInput(string templateCode) => new()
    {
        TemplateCode = templateCode,
        LabelKind = templateCode == LabelTemplateCode.LocDeskBox ? LocDeskLabelKind.Box : LocDeskLabelKind.Folder,
        ArchiveTitle = "ARQUIVO LOCDESCK ANANINDEUA",
        Contract = "Hosp. Ophir Loyola",
        MedicalRecordNumber = "100.334",
        ControlNumber = "199",
        VolumeNumber = 1,
        VolumeTotal = 1,
        Subject = "PRONTUÁRIO nº: 100.334",
        Details = "DAME - ALTA MEDICA",
        Activity = "FIM",
        Classification = "HOL.132.3 - LAUDO DE PROCEDIMENTOS DIAGNÓSTICOS",
        Support = "1. PAPEL",
        PeriodStart = new DateTime(2017, 7, 15),
        PeriodEnd = new DateTime(2017, 9, 25),
        DocumentPeriod = "15/07/2017 A 25/09/2017",
        CurrentPhase = "2. GUARDA INTERMEDIÁRIA",
        EliminationForecast = "0. GUARDA PERMANENTE",
        EliminationStatus = "0. GUARDA PERMANENTE",
        LedNumber = "N/A",
        Location = "LOC.AN.___.E___.P___"
    };
}
