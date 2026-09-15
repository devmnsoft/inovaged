using InovaGed.Application.Labels.Canvas;

namespace InovaGed.Application.Labels.Intelligence;

public static class LabelPreflightSeverity
{
    public const string Error = "ERROR";
    public const string Warning = "WARNING";
    public const string Information = "INFO";
    public const string Recommendation = "RECOMMENDATION";
}

public static class LabelPreflightCategory
{
    public const string Content = "CONTENT";
    public const string Layout = "LAYOUT";
    public const string Branding = "BRANDING";
    public const string Subject = "SUBJECT";
    public const string Printing = "PRINTING";
    public const string Calibration = "CALIBRATION";
}

public sealed record LabelPreflightRequest(Guid TenantId, Guid UserId, string TemplateKey,
    string SubjectType, Guid SubjectId, Guid? BrandingProfileId = null, Guid? CalibrationProfileId = null,
    string? PrinterName = null, string? PaperKind = null, int Copies = 1, string? DesignJson = null,
    bool AllowDraftPreview = false);

public sealed record LabelPreflightItem(string Code, string Severity, string Title, string Message,
    string? ElementId = null, string? SuggestedAction = null, bool CanAutoFix = false, string? AutoFixKey = null,
    string Category = LabelPreflightCategory.Content);

public sealed class LabelPreflightResult
{
    public IReadOnlyList<LabelPreflightItem> Items { get; init; } = [];
    public bool CanPrint => Errors.Count == 0;
    public IReadOnlyList<LabelPreflightItem> Errors => By(LabelPreflightSeverity.Error);
    public IReadOnlyList<LabelPreflightItem> Warnings => By(LabelPreflightSeverity.Warning);
    public IReadOnlyList<LabelPreflightItem> Information => By(LabelPreflightSeverity.Information);
    public IReadOnlyList<LabelPreflightItem> Recommendations => By(LabelPreflightSeverity.Recommendation);
    private IReadOnlyList<LabelPreflightItem> By(string severity) => Items.Where(x => x.Severity == severity).ToArray();
}

public interface ILabelPreflightService
{
    Task<LabelPreflightResult> CheckAsync(LabelPreflightRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LabelPreflightResult>> CheckBatchAsync(IReadOnlyList<LabelPreflightRequest> requests, CancellationToken cancellationToken = default);
    Task<LabelBatchPreflightResult> CheckBatchDetailedAsync(LabelBatchPreflightRequest request, IProgress<LabelBatchPreflightProgress>? progress = null, CancellationToken cancellationToken = default);
}

public sealed record LabelBatchPreflightRequest(IReadOnlyList<LabelPreflightRequest> Items, int MaxSelection = 200,
    int MaxConcurrency = 4);
public sealed record LabelBatchPreflightProgress(int Completed, int Total);
public sealed record LabelBatchPreflightItem(Guid SubjectId, string Status, LabelPreflightResult Result);
public sealed record LabelBatchPreflightSummary(int Total, int Ready, int WithWarnings, int Blocked,
    int ReadinessPercent, TimeSpan Duration, DateTimeOffset ValidatedAt);
public sealed record LabelBatchPreflightResult(IReadOnlyList<LabelBatchPreflightItem> Items, LabelBatchPreflightSummary Summary);

public sealed record LabelTemplateRecommendationCandidate(string TemplateKey, string SubjectType, string PaperKind,
    bool Published, bool Healthy, bool RequiredBindingsAvailable, bool BrandingCompatible = false,
    bool CalibrationCompatible = false, bool UsedSuccessfullyBefore = false);
public sealed record LabelTemplateRecommendationRequest(Guid TenantId, string SubjectType, string? PaperKind,
    IReadOnlyList<LabelTemplateRecommendationCandidate> Candidates);
public sealed record LabelTemplateRecommendation(string TemplateKey, int Score, bool IsCompatible, IReadOnlyList<string> Reasons);

public interface ILabelTemplateRecommendationService
{
    IReadOnlyList<LabelTemplateRecommendation> Recommend(LabelTemplateRecommendationRequest request);
}

public sealed record LabelPrintProfileCandidate(Guid Id, string Name, string? PrinterName, string PaperKind,
    decimal LabelWidthMm, decimal LabelHeightMm, decimal ScalePercent, bool IsTenantDefault, bool UsedSuccessfullyBefore);
public sealed record LabelPrintProfileRecommendation(Guid ProfileId, string Name, int Score, string Reason);

public interface ILabelPrintProfileRecommendationService
{
    LabelPrintProfileRecommendation? Recommend(Guid tenantId, string paperKind, decimal widthMm, decimal heightMm,
        string? printerName, IReadOnlyList<LabelPrintProfileCandidate> profiles);
}

public static class LabelConditionPolicy
{
    public static readonly IReadOnlySet<string> Operators = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "ALWAYS", "HAS_VALUE", "EQUALS", "NOT_EQUALS", "CONTAINS", "NOT_CONTAINS", "IS_EMPTY", "IS_NOT_EMPTY" };
    public static readonly IReadOnlySet<string> ConditionalStyleProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "fontWeight", "color", "backgroundColor", "border" };

    public static bool Evaluate(string? op, object? actual, string? expected)
    {
        var value = Convert.ToString(actual) ?? "";
        return (op ?? "ALWAYS").ToUpperInvariant() switch
        {
            "ALWAYS" => true, "HAS_VALUE" or "IS_NOT_EMPTY" => !string.IsNullOrWhiteSpace(value),
            "IS_EMPTY" => string.IsNullOrWhiteSpace(value),
            "EQUALS" => value.Equals(expected ?? "", StringComparison.OrdinalIgnoreCase),
            "NOT_EQUALS" => !value.Equals(expected ?? "", StringComparison.OrdinalIgnoreCase),
            "CONTAINS" => value.Contains(expected ?? "", StringComparison.OrdinalIgnoreCase),
            "NOT_CONTAINS" => !value.Contains(expected ?? "", StringComparison.OrdinalIgnoreCase),
            _ => throw new ArgumentException("O operador de condição não é permitido.", nameof(op))
        };
    }
}

public enum LabelAutoLayoutAction { AlignLeft, DistributeHorizontal, DistributeVertical, FitSafeMargin, CenterContent }
public static class LabelAutoLayoutAssistant
{
    public static LabelCanvasDocumentDto Preview(LabelCanvasDocumentDto source, LabelAutoLayoutAction action)
    {
        var clone = System.Text.Json.JsonSerializer.Deserialize<LabelCanvasDocumentDto>(
            System.Text.Json.JsonSerializer.Serialize(source)) ?? new();
        var items = clone.Elements.Where(x => !x.Locked).ToArray();
        if (items.Length == 0) return clone;
        var margin = Math.Max(0, clone.Canvas.SafeMarginMm);
        if (action == LabelAutoLayoutAction.AlignLeft) { var x = items.Min(e => e.XMm); foreach (var e in items) e.XMm = x; }
        if (action == LabelAutoLayoutAction.CenterContent)
        {
            var left=items.Min(e=>e.XMm); var right=items.Max(e=>e.XMm+e.WidthMm);
            var shift=(clone.Canvas.WidthMm-(right-left))/2-left; foreach(var e in items)e.XMm+=shift;
        }
        if ((action is LabelAutoLayoutAction.DistributeHorizontal or LabelAutoLayoutAction.DistributeVertical) && items.Length > 2)
        {
            var horizontal=action==LabelAutoLayoutAction.DistributeHorizontal;
            var ordered=horizontal?items.OrderBy(e=>e.XMm).ToArray():items.OrderBy(e=>e.YMm).ToArray();
            var start=horizontal?ordered[0].XMm:ordered[0].YMm; var end=horizontal?ordered[^1].XMm:ordered[^1].YMm;
            var step=(end-start)/(ordered.Length-1); for(var i=1;i<ordered.Length-1;i++)if(horizontal)ordered[i].XMm=start+step*i;else ordered[i].YMm=start+step*i;
        }
        if (action == LabelAutoLayoutAction.FitSafeMargin) foreach(var e in items)
        {
            e.XMm=Math.Clamp(e.XMm,margin,Math.Max(margin,clone.Canvas.WidthMm-margin-e.WidthMm));
            e.YMm=Math.Clamp(e.YMm,margin,Math.Max(margin,clone.Canvas.HeightMm-margin-e.HeightMm));
        }
        return clone;
    }

    public static decimal SafeFontSize(decimal current, decimal requiredScale, decimal minimum = 6m) =>
        Math.Max(minimum, decimal.Round(current * Math.Clamp(requiredScale, 0m, 1m), 1));
}
