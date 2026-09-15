using System.Text.Json.Serialization;
using InovaGed.Application.Branding;

namespace InovaGed.Application.Labels.Canvas;

public static class LabelCanvasDimensionPolicy
{
    public const decimal MinWidthMm = 20m;
    public const decimal MaxWidthMm = 500m;
    public const decimal MinHeightMm = 20m;
    public const decimal MaxHeightMm = 500m;
    public const decimal LegacyMismatchToleranceMm = 0.01m;
}

public sealed class LabelCanvasRequestException(string code, string message, IReadOnlyDictionary<string, string>? errors = null)
    : ArgumentException(message)
{
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, string> Errors { get; } = errors ?? new Dictionary<string, string>();
}

public sealed class LabelCanvasConflictException() : InvalidOperationException("Este modelo foi alterado por outra pessoa.");

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
    public Guid? DefaultBrandingProfileId { get; init; }
    public string? BrandingBindingKey { get; init; }
    public string? ClientNameFallback { get; init; }
    public string? ContractNameFallback { get; init; }
    public string? OrganizationNameFallback { get; init; }
    public string? HeaderTitleFallback { get; init; }
    public string? HeaderSubtitleFallback { get; init; }
    public string LabelContext { get; init; } = "GENERIC";
    public Guid? CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; }
    public Guid? UpdatedBy { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public Guid? PublishedBy { get; init; }
    public DateTime? PublishedAt { get; init; }
    public bool HasPublishedVersion { get; init; }
    public int? PublishedVersionNo { get; init; }
    public bool IsRevision => Status.Equals("DRAFT", StringComparison.OrdinalIgnoreCase) && HasPublishedVersion;
    public string? LayoutHash { get; init; }
    public string? VersionHash { get; init; }
    public long LockVersion { get; init; } = 1;
    public bool CanEdit => !IsSystemTemplate && Status.Equals("DRAFT", StringComparison.OrdinalIgnoreCase);
}

public sealed class LabelCanvasVersionSnapshotDto
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
    public Guid? DefaultBrandingProfileId { get; init; }
    public string? BrandingBindingKey { get; init; }
    public string? ClientNameFallback { get; init; }
    public string? ContractNameFallback { get; init; }
    public string? OrganizationNameFallback { get; init; }
    public string? HeaderTitleFallback { get; init; }
    public string? HeaderSubtitleFallback { get; init; }
    public string LabelContext { get; init; } = "GENERIC";
    public string DesignJson { get; init; } = "{}";
    public int VersionNo { get; init; }
    public string? LayoutHash { get; init; }
    public string? VersionHash { get; init; }
    public string HashScope { get; init; } = "VERSION_ENVELOPE_V2";
}

public sealed class LabelCanvasDocumentDto
{
    public int SchemaVersion { get; set; } = 2;
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
    public string? GroupId { get; set; }
    public string? Text { get; set; }
    public LabelCanvasStyleDto Style { get; set; } = new();
    public LabelCanvasBindingDto? Binding { get; set; }
    public LabelCanvasElementValidationDto Validation { get; set; } = new();
    public LabelCanvasVisibilityConditionDto VisibilityCondition { get; set; } = new();
    public LabelCanvasConditionalAppearanceDto ConditionalAppearance { get; set; } = new();
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
    public string FontStyle { get; set; } = "normal";
    public string TextDecoration { get; set; } = "none";
    public decimal LineHeight { get; set; } = 1.2m;
    public decimal LetterSpacing { get; set; }
    public decimal Opacity { get; set; } = 1m;
    public string VerticalAlign { get; set; } = "top";
}

public sealed class LabelCanvasBindingDto
{
    public string? Field { get; set; }
    public string? Asset { get; set; }
    public string? Fallback { get; set; }
    public string? Prefix { get; set; }
    public string? Suffix { get; set; }
    public string Format { get; set; } = "NONE";
    public string EmptyBehavior { get; set; } = "FALLBACK";
    public int? PadLength { get; set; }
}

public sealed class LabelCanvasVisibilityConditionDto
{
    public string Operator { get; set; } = "ALWAYS";
    public string? Field { get; set; }
    public string? Value { get; set; }
}

/// <summary>Declarative appearance rule restricted to renderer-supported styles.</summary>
public sealed class LabelCanvasConditionalAppearanceDto
{
    public bool Enabled { get; set; }
    public string Operator { get; set; } = "EQUALS";
    public string? Field { get; set; }
    public string? Value { get; set; }
    public string Preset { get; set; } = "HIGHLIGHT";
    public string? FontWeight { get; set; }
    public string? Color { get; set; }
    public string? BackgroundColor { get; set; }
    public string? Border { get; set; }
}

public sealed class LabelCanvasElementValidationDto
{
    public bool Required { get; set; }
    public int? MaxCharacters { get; set; }
    public bool ShowOnlyWhenValue { get; set; }
}

public sealed class LabelCanvasSaveRequest
{
    public long? ExpectedLockVersion { get; init; }
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
    public Guid? DefaultBrandingProfileId { get; init; }
    public string? BrandingBindingKey { get; init; }
    public string? ClientNameFallback { get; init; }
    public string? ContractNameFallback { get; init; }
    public string? OrganizationNameFallback { get; init; }
    public string? HeaderTitleFallback { get; init; }
    public string? HeaderSubtitleFallback { get; init; }
    public string LabelContext { get; init; } = "GENERIC";
    public string? ChangeSummary { get; init; }
}

public sealed class LabelCanvasPublishRequest
{
    public string TemplateKey { get; init; } = "";
    public string? ChangeSummary { get; init; }
    public bool ConfirmWarnings { get; init; }
    public string? WarningJustification { get; init; }
    public long? ExpectedLockVersion { get; init; }
    public int ApprovedTestSamples { get; init; }
    public decimal? MediaWidthMm { get; init; }
    public decimal? MediaHeightMm { get; init; }
}

public static class LabelPublicationSeverity
{
    public const string Blocker = "BLOCKER";
    public const string Warning = "WARNING";
    public const string Information = "INFORMATION";
}

public sealed record LabelPublicationCheck(string Code, string Severity, string Category, string Message,
    string? ElementId = null, string? SuggestedAction = null);

public sealed record LabelPublicationChecklist(IReadOnlyList<LabelPublicationCheck> Items)
{
    public int Blockers => Items.Count(x => x.Severity == LabelPublicationSeverity.Blocker);
    public int Warnings => Items.Count(x => x.Severity == LabelPublicationSeverity.Warning);
    public int Information => Items.Count(x => x.Severity == LabelPublicationSeverity.Information);
    public int ReadinessPercent => Math.Clamp(100 - Blockers * 25 - Warnings * 5, 0, 100);
    public bool CanPublish => Blockers == 0;
}

public interface ILabelPublicationChecklistService
{
    LabelPublicationChecklist Evaluate(LabelCanvasDesignDto design, LabelCanvasPublishRequest request,
        IReadOnlySet<string> allowedFields);
}

public sealed class LabelCanvasPreviewRequest
{
    public string TemplateKey { get; init; } = "";
    public string? DesignJson { get; init; }
    public string SampleDataProfile { get; init; } = "HOL";
    public IReadOnlyDictionary<string, object?>? SampleData { get; init; }
}

public sealed record LabelCanvasPreviewSubjectDto(Guid Id, string Reference, string PrimaryText, string? SecondaryText);

public sealed class LabelCanvasPreviewSubjectRequest
{
    public Guid SubjectId { get; init; }
    public string DesignJson { get; init; } = "{}";
    public Guid? BrandingProfileId { get; init; }
}

public sealed class LabelCanvasTestLabRequest
{
    public IReadOnlyList<Guid> SubjectIds { get; init; } = [];
    public string DesignJson { get; init; } = "{}";
    public Guid? BrandingProfileId { get; init; }
}

public sealed class LabelCanvasStarterPreviewRequest
{
    public string SubjectType { get; init; } = "Document";
    public string StarterKind { get; init; } = "Blank";
    public decimal WidthMm { get; init; } = 100;
    public decimal HeightMm { get; init; } = 70;
    public Guid? BrandingProfileId { get; init; }
}

public sealed class LabelCanvasValidationResult
{
    public List<LabelCanvasValidationIssue> Issues { get; init; } = [];
    [JsonIgnore] public bool HasErrors => Issues.Any(x => x.Severity == "ERROR");
    [JsonIgnore] public bool HasWarnings => Issues.Any(x => x.Severity == "WARNING");
    [JsonIgnore] public bool IsValid => !HasErrors;
}

public sealed record LabelCanvasValidationIssue(string Code, string Severity, string Message, string? ElementId = null);
public sealed record LabelCanvasFieldDto(string Key, string Label, string DataType, string SubjectType, string? Example = null, string? Category = null, string? Description = null, bool IsEditableInManualMode = false);

public enum LabelCanvasStarterKind { Blank, Institutional, IdentificationQr, Classification, Traceability, ManualLabel }
public sealed record LabelCanvasStarterRequest(string SubjectType, LabelCanvasStarterKind StarterKind, decimal WidthMm, decimal HeightMm, Guid? BrandingProfileId, string PaperKind = "A4");
public interface ILabelCanvasStarterTemplateService
{
    LabelCanvasDocumentDto Create(LabelCanvasStarterRequest request);
}

public static class LabelPaperOptions
{
    public static readonly IReadOnlyList<string> Supported = new[] { "A4", "A5", "LETTER", "CUSTOM" };
    public static bool IsSupported(string? value) => Supported.Contains(value ?? "", StringComparer.OrdinalIgnoreCase);
}
public sealed record LabelCanvasVersionDto(Guid Id, int VersionNo, string Status, string? ChangeSummary, Guid? CreatedBy, DateTime CreatedAt, Guid? PublishedBy, DateTime? PublishedAt, string? SnapshotHash, string? CreatedByName = null, string? PublishedByName = null);
public sealed record LabelCanvasRenderResult(string Html, string SnapshotHash, LabelCanvasValidationResult Validation);

public interface ILabelCanvasDesignService
{
    Task<IReadOnlyList<LabelCanvasDesignDto>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<LabelCanvasDesignDto?> GetAsync(Guid tenantId, string templateKey, CancellationToken cancellationToken = default);
    Task<LabelCanvasDesignDto?> GetPublishedAsync(Guid tenantId, string templateKey, int? versionNo = null, CancellationToken cancellationToken = default);
    Task<LabelCanvasDesignDto> BeginRevisionAsync(Guid tenantId, Guid userId, string templateKey, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<LabelCanvasDesignDto> CancelRevisionAsync(Guid tenantId, Guid userId, string templateKey, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
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

public enum LabelCanvasExecutionMode { Production, Preview, Demo, SnapshotReplay }

public static class LabelCanvasSubjectTypeMapper
{
    public static string ToOperational(string? subjectType) => subjectType?.Trim().ToUpperInvariant() switch
    {
        "BOX" or "LOCDESKBOX" => "BOX",
        "DOCUMENT" or "FOLDER" or "MEDICALRECORD" or "PROCESS" or "LOCDESKFOLDER" => "DOCUMENT",
        "BATCH" => "BATCH",
        "MANUALLABEL" or "MANUAL_LABEL" => "MANUAL_LABEL",
        { Length: > 0 } value => value,
        _ => "DOCUMENT"
    };
}

public sealed record LabelCanvasCalibration(
    Guid? ProfileId,
    decimal MarginTopMm,
    decimal MarginLeftMm,
    decimal OffsetXMm,
    decimal OffsetYMm,
    decimal ScalePercent,
    decimal GapXMm,
    decimal GapYMm);

public sealed class LabelCanvasPrintContext
{
    public LabelCanvasExecutionMode ExecutionMode { get; init; } = LabelCanvasExecutionMode.Production;
    public Guid TenantId { get; init; }
    public string TemplateKey { get; init; } = "";
    public string SubjectType { get; init; } = "Document";
    public string OperationalSubjectType { get; init; } = "DOCUMENT";
    public Guid? SubjectId { get; init; }
    public Guid? BrandingProfileId { get; init; }
    public Guid? PrintProfileId { get; init; }
    public Guid? SelectedLogoAssetId { get; init; }
    public int Copies { get; init; } = 1;
    public bool RegisterTrace { get; init; }
    public string? ReprintReason { get; init; }
    public int? TemplateVersion { get; init; }
    public string? RegisteredTraceCode { get; init; }
    public string? RegisteredTraceUrl { get; init; }
    public string? AbsoluteBaseUrl { get; init; }
    public string? PrintedBy { get; init; }
    public IReadOnlyDictionary<string, object?>? ResolvedValues { get; init; }
    public ResolvedPrintBranding? BrandingSnapshot { get; init; }
    public LabelCanvasCalibration? CalibrationSnapshot { get; init; }
}

public sealed record LabelCanvasPreparedRender(
    LabelCanvasDesignDto Design,
    int TemplateVersion,
    IReadOnlyDictionary<string, object?> Values,
    ResolvedPrintBranding Branding,
    LabelCanvasCalibration Calibration,
    string Html,
    string SnapshotHash,
    LabelCanvasValidationResult Validation)
{
    public IReadOnlyDictionary<string,object?> CreateSnapshot(LabelCanvasPrintContext context,string printChannel)=>new Dictionary<string,object?>
    {
        ["layoutSource"]="CANVAS",["isDesignerTemplate"]=true,["templateCode"]=Design.TemplateKey,["templateName"]=Design.TemplateName,
        ["templateVersion"]=TemplateVersion,["snapshotHash"]=SnapshotHash,["subjectType"]=context.OperationalSubjectType,["subjectId"]=context.SubjectId,
        ["printChannel"]=printChannel,["copies"]=context.Copies,["branding"]=new{profileId=Branding.ProfileId,profileName=Branding.ProfileName,clientName=Values.GetValueOrDefault("clientName"),contractName=Values.GetValueOrDefault("contractName"),organizationName=Values.GetValueOrDefault("organizationName"),headerTitle=Values.GetValueOrDefault("headerTitle"),headerSubtitle=Values.GetValueOrDefault("headerSubtitle"),headerExtraLine=Values.GetValueOrDefault("headerExtraLine"),footerText=Values.GetValueOrDefault("footerText"),footerExtraLine=Values.GetValueOrDefault("footerExtraLine"),primaryLogoAssetId=Branding.PrimaryLogoAssetId,secondaryLogoAssetId=Branding.SecondaryLogoAssetId},
        ["calibration"]=Calibration,["printedFields"]=Values,["traceCode"]=Values.TryGetValue("traceCode",out var trace)?trace:null
    };
}

public interface ILabelCanvasPrintCoordinator
{
    Task<LabelCanvasPreparedRender> PrepareAsync(LabelCanvasPrintContext context, bool printMode = false, CancellationToken cancellationToken = default);
    Task<LabelCanvasPreparedRender> PrepareBatchAsync(IReadOnlyList<LabelCanvasPrintContext> contexts, bool printMode = false, CancellationToken cancellationToken = default);
}

public interface ILabelCanvasRenderService
{
    LabelCanvasValidationResult Validate(string designJson, IReadOnlySet<string>? allowedFields = null);
    LabelCanvasRenderResult Render(LabelCanvasDesignDto design, IReadOnlyDictionary<string, object?> values, bool printMode = false);
    LabelCanvasRenderResult RenderBatch(LabelCanvasDesignDto design, IReadOnlyList<IReadOnlyDictionary<string, object?>> values, bool printMode = false);
    string ComputeSnapshotHash(string designJson);
}

public interface ILabelCanvasValueResolver
{
    Task<IReadOnlyDictionary<string, object?>> ResolveAsync(
        Guid tenantId,
        string designerSubjectType,
        string operationalSubjectType,
        Guid subjectId,
        CancellationToken cancellationToken = default);
}

public interface ILabelCanvasFieldCatalogService
{
    IReadOnlyList<LabelCanvasFieldDto> GetFields(string subjectType);
    IReadOnlyDictionary<string, object?> GetSampleData(string profile);
}

public sealed record LabelCanvasSchemaCapabilitySnapshot(
    bool HasLabelTemplateDesign,
    bool HasLockVersion,
    bool HasComponentPreset,
    bool HasPrintSelection);

public interface ILabelCanvasSchemaCapabilities
{
    Task<LabelCanvasSchemaCapabilitySnapshot> GetAsync(CancellationToken cancellationToken = default);
}

public sealed record LabelCanvasComponentPresetDto(Guid Id, string Name, string Category, int SchemaVersion,
    int ElementCount, DateTime CreatedAt, DateTime? UpdatedAt);
public sealed record LabelCanvasComponentPresetDetailsDto(Guid Id, string Name, string Category, int SchemaVersion,
    int ElementCount, DateTime CreatedAt, DateTime? UpdatedAt, string ElementsJson);
public sealed record LabelCanvasComponentPresetCreateRequest(string Name, string Category, string ElementsJson, int SchemaVersion = 2);

public interface ILabelCanvasComponentPresetService
{
    Task<IReadOnlyList<LabelCanvasComponentPresetDto>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<LabelCanvasComponentPresetDetailsDto?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default);
    Task<LabelCanvasComponentPresetDetailsDto> CreateAsync(Guid tenantId, Guid userId, LabelCanvasComponentPresetCreateRequest request, CancellationToken cancellationToken = default);
    Task<LabelCanvasComponentPresetDto> RenameAsync(Guid tenantId, Guid userId, Guid id, string name, CancellationToken cancellationToken = default);
    Task<LabelCanvasComponentPresetDetailsDto> DuplicateAsync(Guid tenantId, Guid userId, Guid id, string? name = null, CancellationToken cancellationToken = default);
    Task ArchiveAsync(Guid tenantId, Guid userId, Guid id, CancellationToken cancellationToken = default);
}

public sealed record LabelTemplatePackageTemplate(string Name, string? Description, string SubjectType, string PaperKind,
    decimal WidthMm, decimal HeightMm, string Orientation);
public sealed record LabelTemplatePackage(int PackageVersion, int SchemaVersion, LabelTemplatePackageTemplate Template,
    LabelCanvasDocumentDto Design);

public interface ILabelTemplatePackageService
{
    LabelTemplatePackage Export(LabelCanvasDesignDto design);
    LabelTemplatePackage Validate(string packageJson, ILabelCanvasFieldCatalogService fields);
}

public sealed class LabelSchemaUpdateRequiredException : InvalidOperationException
{
    public const string Code = "LABEL_SCHEMA_UPDATE_REQUIRED";
    public const string FriendlyMessage = "O módulo de etiquetas precisa de uma atualização de banco antes de permitir alterações.";
    public LabelSchemaUpdateRequiredException() : base(FriendlyMessage) { }
}
