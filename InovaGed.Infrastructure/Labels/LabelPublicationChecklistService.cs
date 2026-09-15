using InovaGed.Application.Labels.Canvas;

namespace InovaGed.Infrastructure.Labels;

/// <summary>Single, side-effect-free publication gate built on the production renderer.</summary>
public sealed class LabelPublicationChecklistService(ILabelCanvasRenderService renderer) : ILabelPublicationChecklistService
{
    public LabelPublicationChecklist Evaluate(LabelCanvasDesignDto design, LabelCanvasPublishRequest request,
        IReadOnlySet<string> allowedFields)
    {
        var items = renderer.Validate(design.DesignJson, allowedFields).Issues.Select(issue => new LabelPublicationCheck(
            issue.Code,
            issue.Severity == "ERROR" ? LabelPublicationSeverity.Blocker : LabelPublicationSeverity.Warning,
            Category(issue.Code), issue.Message, issue.ElementId,
            issue.ElementId is null ? "template" : "designer")).ToList();

        if (design.WidthMm <= 0 || design.HeightMm <= 0)
            items.Add(new("MODEL_DIMENSIONS", LabelPublicationSeverity.Blocker, "PRINTING", "As dimensões físicas do modelo são inválidas."));
        if (request.MediaWidthMm is decimal mediaWidth && request.MediaHeightMm is decimal mediaHeight &&
            (Math.Abs(mediaWidth - design.WidthMm) > .1m || Math.Abs(mediaHeight - design.HeightMm) > .1m))
            items.Add(new("MEDIA_MISMATCH", LabelPublicationSeverity.Blocker, "PRINTING", "A mídia selecionada não corresponde às dimensões do modelo.", SuggestedAction: "printer"));
        if (request.ApprovedTestSamples < 1)
            items.Add(new("TEST_LAB_REQUIRED", LabelPublicationSeverity.Blocker, "CONTENT", "A publicação exige ao menos uma amostra aprovada no Laboratório de Testes.", SuggestedAction: "test-lab"));
        if (request.ExpectedLockVersion is null || request.ExpectedLockVersion != design.LockVersion)
            items.Add(new("STALE_VERSION", LabelPublicationSeverity.Blocker, "SECURITY", "O rascunho foi alterado. Recarregue antes de publicar."));
        if (!items.Any())
            items.Add(new("READY", LabelPublicationSeverity.Information, "CONTENT", "Modelo tecnicamente apto para publicação."));
        return new(items);
    }

    private static string Category(string code) => code switch
    {
        "INVALID_ASSET" => "IDENTITY",
        "UNSAFE_FORMAT" or "UNSAFE_VISIBILITY" or "UNSAFE_CONDITIONAL_APPEARANCE" or "UNSAFE_CONDITIONAL_STYLE" => "SECURITY",
        "CANVAS_SIZE" or "OUTSIDE_CANVAS" or "SAFE_MARGIN" or "CRITICAL_OVERLAP" or "TEXT_OVERFLOW" => "LAYOUT",
        _ => "CONTENT"
    };
}
