using InovaGed.Application.Labels.Canvas;
using InovaGed.Infrastructure.Labels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace InovaGed.Application.Tests;

public sealed class LabelOperationalHomologationRc45Tests
{
    private static readonly LabelCanvasRenderService Renderer = new(NullLogger<LabelCanvasRenderService>.Instance);
    private static readonly LabelPublicationChecklistService Checklist = new(Renderer);

    [Fact]
    public void Publication_is_blocked_without_approved_real_sample()
    {
        var design = ValidDesign();
        var result = Checklist.Evaluate(design,
            new LabelCanvasPublishRequest { ExpectedLockVersion = design.LockVersion }, new HashSet<string>());

        Assert.False(result.CanPublish);
        Assert.Contains(result.Items, x => x.Code == "TEST_LAB_REQUIRED" && x.Severity == LabelPublicationSeverity.Blocker);
    }

    [Fact]
    public void Publication_is_blocked_for_stale_lock_or_incompatible_media()
    {
        var design = ValidDesign();
        var result = Checklist.Evaluate(design, new LabelCanvasPublishRequest
        {
            ExpectedLockVersion = design.LockVersion - 1, ApprovedTestSamples = 1,
            MediaWidthMm = 90, MediaHeightMm = 50
        }, new HashSet<string>());

        Assert.Contains(result.Items, x => x.Code == "STALE_VERSION");
        Assert.Contains(result.Items, x => x.Code == "MEDIA_MISMATCH");
    }

    [Fact]
    public void Validated_model_reports_full_readiness()
    {
        var design = ValidDesign();
        var result = Checklist.Evaluate(design, new LabelCanvasPublishRequest
        {
            ExpectedLockVersion = design.LockVersion, ApprovedTestSamples = 1,
            MediaWidthMm = 100, MediaHeightMm = 70
        }, new HashSet<string>());

        Assert.True(result.CanPublish);
        Assert.Equal(100, result.ReadinessPercent);
        Assert.Contains(result.Items, x => x.Code == "READY");
    }

    private static LabelCanvasDesignDto ValidDesign() => new()
    {
        Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), TemplateKey = "RC45", TemplateName = "RC45",
        Status = "DRAFT", WidthMm = 100, HeightMm = 70, LockVersion = 7,
        DesignJson = """{"schemaVersion":2,"canvas":{"widthMm":100,"heightMm":70,"safeMarginMm":0},"bindings":{"subjectType":"Document"},"elements":[]}"""
    };
}
