namespace InovaGed.Application.Branding;

public static class PrintBrandingContext
{
    public const string LabelTemplate = "LABEL_TEMPLATE";
    public const string DocumentReport = "DOCUMENT_REPORT";
    public const string ContractMeasurement = "CONTRACT_MEASUREMENT";
    public const string FiscalPortal = "FISCAL_PORTAL";
    public const string GovernanceReport = "GOVERNANCE_REPORT";
    public const string DocumentCover = "DOCUMENT_COVER";
    public const string DemoSample = "DEMO_SAMPLE";
}

public sealed class ResolvedPrintBranding
{
    public bool HasBranding { get; init; }
    public string Message { get; init; } = "Nenhuma identidade visual configurada. A impressão seguirá sem logo.";
    public Guid? ProfileId { get; init; }
    public string? ProfileName { get; init; }
    public Guid? PrimaryLogoAssetId { get; init; }
    public Guid? SecondaryLogoAssetId { get; init; }
    public string? HeaderTitle { get; init; }
    public string? HeaderSubtitle { get; init; }
    public string? HeaderExtraLine { get; init; }
    public string? FooterText { get; init; }
    public string? FooterExtraLine { get; init; }
    public decimal PrimaryLogoWidthMm { get; init; } = 38;
    public decimal SecondaryLogoWidthMm { get; init; } = 28;
    public bool ShowGeneratedAt { get; init; } = true;
    public bool ShowPageNumber { get; init; } = true;
}

public interface IPrintBrandingResolver
{
    Task<ResolvedPrintBranding> ResolveAsync(Guid tenantId, string context, string bindingKey, Guid? selectedProfileId, Guid? selectedLogoAssetId, CancellationToken ct);
}
