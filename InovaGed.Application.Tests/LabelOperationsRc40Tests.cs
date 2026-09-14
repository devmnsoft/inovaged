using InovaGed.Application.Labels.Printing;

namespace InovaGed.Application.Tests;

public sealed class LabelOperationsRc40Tests
{
    [Fact] public void pending_actions_are_valid() => Assert.Equal("preview", LabelPrintJobPresentation.Actions(LabelPrintJobStatus.Pending, false).Single(x => x.IsPrimary).Key);
    [Fact] public void printed_has_no_mark_printed_action() => Assert.DoesNotContain(LabelPrintJobPresentation.Actions(LabelPrintJobStatus.Printed, true), x => x.Key == "mark-printed");
    [Fact] public void cancelled_has_no_operational_action() => Assert.Empty(LabelPrintJobPresentation.Actions(LabelPrintJobStatus.Cancelled, false));
    [Fact] public void error_shows_retry_only_when_retryable() => Assert.Equal("retry", LabelPrintJobPresentation.Actions(LabelPrintJobStatus.Error, false).Single().Key);

    [Theory]
    [InlineData(LabelPrintJobStatus.Pending, "created")]
    [InlineData(LabelPrintJobStatus.Error, "error")]
    [InlineData(LabelPrintJobStatus.Printed, "printed")]
    public void job_timeline_reflects_real_state(string status, string expected)
    {
        var now = DateTime.UtcNow;
        var job = Job(status, status == LabelPrintJobStatus.Printed ? now : null, status == LabelPrintJobStatus.Printed ? now.AddMinutes(-1) : null);
        Assert.Contains(LabelPrintJobPresentation.Timeline(job), x => x.Key == expected);
    }

    [Fact] public void job_timeline_reprint() => Assert.Contains(LabelPrintJobPresentation.Timeline(Job(LabelPrintJobStatus.Pending, null, null, "REPRINT_EXACT", "Danificada")), x => x.Key == "reprint");
    [Fact] public void job_timeline_artifact_generated() => Assert.Contains(LabelPrintJobPresentation.Timeline(Job(LabelPrintJobStatus.PdfGenerated, null, DateTime.UtcNow)), x => x.Key == "artifact");
    [Fact] public void calibration_wizard_scale_calculation() { Assert.True(LabelCalibrationCalculator.TrySuggestScale("48", out var value)); Assert.Equal(104.17m, value); }
    [Fact] public void calibration_rejects_absurd_scale() => Assert.False(LabelCalibrationCalculator.TrySuggestScale("10", out _));
    [Theory] [InlineData("48,2")] [InlineData("48.2")] public void calibration_accepts_ptbr_decimal(string value) => Assert.True(LabelCalibrationCalculator.TrySuggestScale(value, out _));

    private static LabelPrintJobDetails Job(string status, DateTime? printed, DateTime? artifact, string? operation = null, string? reason = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "LBL-1", "WEB", "BOX", "Caixa padrão", "BOX", Guid.NewGuid(), "CX-1", null, 1, status, "{}", null, status == LabelPrintJobStatus.Error ? "Falha" : null, Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-5), null, printed, null, reason, "Operador", [], "abc", "text/html", 3, artifact, operation);
}
