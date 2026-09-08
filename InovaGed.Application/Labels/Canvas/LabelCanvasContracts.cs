using System.Text.Json.Serialization;

namespace InovaGed.Application.Labels.Canvas;

public sealed class LabelCanvasDesignDto
{
    public Guid Id { get; init; }
    public Guid? TenantId { get; init; }
    public string TemplateKey { get; init; } = "";
    public string TemplateName { get; init; } = "";
    public string? Description { get; init; }
    public string TemplateKind { get; init; } = "CANVAS";
    public string SubjectType { get; init; } = "Document";
    public string PaperKind { get; init; } = "A4";
    public decimal WidthMm { get; init; } = 100;
    public decimal HeightMm { get; init; } = 70;
    public string Orientation { get; init; } = "portrait";
    public string Status { get; init; } = "DRAFT";
    public string DesignJson { get; init; } = "{}";
    public int CurrentVersion { get; init; } = 1;
    public bool IsSystemTemplate { get; init; }
    public Guid? CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; }
    public Guid? UpdatedBy { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public Guid? PublishedBy { get; init; }
    public DateTime? PublishedAt { get; init; }
    public bool CanEdit => !IsSystemTemplate && Status.Equals("DRAFT", StringComparison.OrdinalIgnoreCase);
}

public sealed class LabelCanvasDocumentDto
{
    public int SchemaVersion { get; set; } = 1;
    public LabelCanvasSettingsDto Canvas { get; set; } = new();
    public List<LabelCanvasElementDto> Elements { get; set; } = [];
    public LabelCanvasBindingsDto Bindings { get; set; } = new();
}

public sealed class LabelCanvasSettingsDto
{
    public decimal WidthMm { get; set; } = 100;
    public decimal HeightMm { get; set; } = 70;
    public string Paper { get; set; } = "A4";
    public string Orientation { get; set; } = "portrait";
    public decimal GridMm { get; set; } = 2;
    public decimal SafeMarginMm { get; set; } = 3;
}

public sealed class LabelCanvasBindingsDto
{
    public string SubjectType { get; set; } = "Document";
    public string SampleDataProfile { get; set; } = "Documento GED";
}

public sealed class LabelCanvasElementDto
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "text";
    public string Name { get; set; } = "";
    public decimal XMm { get; set; }
    public decimal YMm { get; set; }
    public decimal WidthMm { get; set; } = 30;
    public decimal HeightMm { get; set; } = 8;
    public decimal RotationDeg { get; set; }
    public int ZIndex { get; set; }
    public bool Locked { get; set; }
    public bool Visible { get; set; } = true;
    public string? Text { get; set; }
    public LabelCanvasStyleDto Style { get; set; } = new();
    public LabelCanvasBindingDto? Binding { get; set; }
    public LabelCanvasElementValidationDto Validation { get; set; } = new();
    public List<LabelCanvasElementDto>? Children { get; set; }
}

public sealed class LabelCanvasStyleDto
{
    public string FontFamily { get; set; } = "Arial";
    public decimal FontSizePt { get; set; } = 9;
    public string FontWeight { get; set; } = "400";
    public string Align { get; set; } = "left";
    public string Color { get; set; } = "#111111";
    public string BackgroundColor { get; set; } = "transparent";
    public string Border { get; set; } = "none";
    public decimal BorderRadiusMm { get; set; }
    public decimal PaddingMm { get; set; }
    public bool Wrap { get; set; } = true;
}

public sealed class LabelCanvasBindingDto
{
    public string? Field { get; set; }
    public string? Asset { get; set; }
    public string? Fallback { get; set; }
}

public sealed class LabelCanvasElementValidationDto
{
    public bool Required { get; set; }
    public int? MaxCharacters { get; set; }
    public bool ShowOnlyWhenValue { get; set; }
}

public sealed class LabelCanvasSaveRequest
{
    public string TemplateKey { get; init; } = "";
    public string TemplateName { get; init; } = "";
    public string? Description { get; init; }
    public string TemplateKind { get; init; } = "CANVAS";
    public string SubjectType { get; init; } = "Document";
    public string PaperKind { get; init; } = "A4";
    public decimal WidthMm { get; init; }
    public decimal HeightMm { get; init; }
    public string Orientation { get; init; } = "portrait";
    public string DesignJson { get; init; } = "{}";
    public string? ChangeSummary { get; init; }
}

public sealed class LabelCanvasPublishRequest
{
    public string TemplateKey { get; init; } = "";
    public string? ChangeSummary { get; init; }
    public bool ConfirmWarnings { get; init; }
}

public sealed class LabelCanvasPreviewRequest
{
    public string TemplateKey { get; init; } = "";
    public string? DesignJson { get; init; }
    public string SampleDataProfile { get; init; } = "HOL";
    public IReadOnlyDictionary<string, object?>? SampleData { get; init; }
}

public sealed class LabelCanvasValidationResult
{
    public List<LabelCanvasValidationIssue> Issues { get; init; } = [];
    [JsonIgnore] public bool HasErrors => Issues.Any(x => x.Severity == "ERROR");
    [JsonIgnore] public bool HasWarnings => Issues.Any(x => x.Severity == "WARNING");
    [JsonIgnore] public bool IsValid => !HasErrors;
}

public sealed record LabelCanvasValidationIssue(string Code, string Severity, string Message, string? ElementId = null);
public sealed record LabelCanvasFieldDto(string Key, string Label, string DataType, string SubjectType, string? Example = null);
public sealed record LabelCanvasVersionDto(Guid Id, int VersionNo, string Status, string? ChangeSummary, Guid? CreatedBy, DateTime CreatedAt, Guid? PublishedBy, DateTime? PublishedAt, string? SnapshotHash, string? CreatedByName = null, string? PublishedByName = null);
public sealed record LabelCanvasRenderResult(string Html, string SnapshotHash, LabelCanvasValidationResult Validation);

public interface ILabelCanvasDesignService
{
    Task<IReadOnlyList<LabelCanvasDesignDto>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<LabelCanvasDesignDto?> GetAsync(Guid tenantId, string templateKey, CancellationToken cancellationToken = default);
    Task<LabelCanvasDesignDto> CreateDraftAsync(Guid tenantId, Guid userId, LabelCanvasSaveRequest request, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<LabelCanvasDesignDto> SaveDraftAsync(Guid tenantId, Guid userId, LabelCanvasSaveRequest request, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<LabelCanvasDesignDto> PublishAsync(Guid tenantId, Guid userId, LabelCanvasPublishRequest request, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<LabelCanvasDesignDto> DuplicateAsync(Guid tenantId, Guid userId, string templateKey, string? newName, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task DeleteDraftAsync(Guid tenantId, Guid userId, string templateKey, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LabelCanvasVersionDto>> GetVersionsAsync(Guid tenantId, string templateKey, CancellationToken cancellationToken = default);
    Task<LabelCanvasDesignDto?> GetVersionDesignAsync(Guid tenantId, string templateKey, Guid versionId, CancellationToken cancellationToken = default);
    Task<LabelCanvasDesignDto> DuplicateVersionAsync(Guid tenantId, Guid userId, string templateKey, Guid versionId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<LabelCanvasDesignDto> RestoreVersionAsync(Guid tenantId, Guid userId, string templateKey, Guid versionId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task RecordEventAsync(Guid tenantId, Guid? userId, Guid designId, string eventType, string? message, object? payload, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
}

public interface ILabelCanvasRenderService
{
    LabelCanvasValidationResult Validate(string designJson, IReadOnlySet<string>? allowedFields = null);
    LabelCanvasRenderResult Render(LabelCanvasDesignDto design, IReadOnlyDictionary<string, object?> values, bool printMode = false);
    string ComputeSnapshotHash(string designJson);
}

public interface ILabelCanvasFieldCatalogService
{
    IReadOnlyList<LabelCanvasFieldDto> GetFields(string subjectType);
    IReadOnlyDictionary<string, object?> GetSampleData(string profile);
}
