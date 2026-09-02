using System.ComponentModel.DataAnnotations;

namespace InovaGed.Web.Models.Labels;

public sealed class LogoLayoutInput
{
    public Guid? LogoAssetId { get; set; }
    [Range(10, 90)] public decimal WidthMm { get; set; } = 38;
    [Range(5, 60)] public decimal? HeightMm { get; set; }
    public bool PreserveAspectRatio { get; set; } = true;
    [RegularExpression("CONTAIN|COVER|FILL")] public string FitMode { get; set; } = "CONTAIN";
    [RegularExpression("TOP_LEFT|TOP_CENTER|TOP_RIGHT|MIDDLE_LEFT|MIDDLE_CENTER|MIDDLE_RIGHT|BOTTOM_LEFT|BOTTOM_CENTER|BOTTOM_RIGHT|CENTER_HEADER|CUSTOM")] public string Position { get; set; } = "TOP_LEFT";
    [Range(-30, 30)] public decimal PositionXMm { get; set; }
    [Range(-30, 30)] public decimal PositionYMm { get; set; }
    [Range(0, 60)] public decimal MarginTopMm { get; set; }
    [Range(0, 90)] public decimal MarginLeftMm { get; set; }
    public bool ApplyToAllCopies { get; set; } = true;
    public bool Enabled { get; set; } = true;
}

public sealed record LogoLayoutAsset(Guid Id, string BrandName, string AssetName, string FileUrl, bool IsDefault, int? WidthPx, int? HeightPx);
public sealed record LogoLayoutValidation(string Severity, string Title, string? Description);
public sealed record LogoLayoutCard(string Code, string Name, string Kind, string Dimensions, string? LogoUrl, string Status);

public sealed class LogoLayoutEditorVm
{
    public string TemplateCode { get; init; } = "";
    public string TemplateName { get; init; } = "";
    public string TemplateKind { get; init; } = "Etiqueta";
    public string Dimensions { get; init; } = "174 × 110 mm";
    public LogoLayoutInput Layout { get; init; } = new();
    public IReadOnlyList<LogoLayoutAsset> Assets { get; init; } = [];
    public IReadOnlyList<LogoLayoutValidation> Validations { get; init; } = [];
    public bool SchemaReady { get; init; }
    public string? SelectedLogoUrl => Layout.LogoAssetId is Guid id ? $"/Administration/BrandAssets/{id}/File" : null;
}
