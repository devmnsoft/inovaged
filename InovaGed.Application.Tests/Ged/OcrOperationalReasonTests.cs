using InovaGed.Application.Ocr;

namespace InovaGed.Application.Tests.Ged;

public sealed class OcrOperationalReasonTests
{
    [Theory]
    [InlineData("QUEUED", null, OcrOperationalReason.Waiting)]
    [InlineData("SKIPPED_PENDING", "PENDING", OcrOperationalReason.Waiting)]
    [InlineData("SKIPPED_PROCESSING", "PROCESSING", OcrOperationalReason.Processing)]
    [InlineData("QUEUED", "COMPLETED", OcrOperationalReason.Completed)]
    [InlineData("SKIPPED_ALREADY_HAS_OCR", null, OcrOperationalReason.Completed)]
    [InlineData("SKIPPED_UNSUPPORTED_EXTENSION", null, OcrOperationalReason.NotEligible)]
    [InlineData("SKIPPED_NO_CURRENT_VERSION", null, OcrOperationalReason.NotEligible)]
    [InlineData("FAILED", "ERROR", OcrOperationalReason.Failed)]
    [InlineData("QUEUED", "FAILED_ENVIRONMENT", OcrOperationalReason.NeedsIntervention)]
    [InlineData("QUEUED", "FAILED_PERMANENT", OcrOperationalReason.NeedsIntervention)]
    [InlineData(null, null, OcrOperationalReason.NotEligible)]
    public void Present_maps_schedule_and_job_to_one_operational_situation(string? schedule, string? job, string expected)
    {
        Assert.Equal(expected, OcrOperationalReason.Present(schedule, job));
    }

    [Fact]
    public void Safe_detail_hides_exception_text_on_failure_and_keeps_skip_reason()
    {
        const string secret = "password=segredo SELECT ocr_text FROM ged.document_search";
        Assert.DoesNotContain("segredo", OcrOperationalReason.SafeDetail(OcrOperationalReason.Failed, secret));
        Assert.DoesNotContain("ocr_text", OcrOperationalReason.SafeDetail(OcrOperationalReason.NeedsIntervention, secret));
        Assert.Equal("Extensão não permitida para OCR automático: .zip.", OcrOperationalReason.SafeDetail(OcrOperationalReason.NotEligible, "Extensão não permitida para OCR automático: .zip."));
        Assert.Equal(OcrOperationalReason.Waiting, OcrOperationalReason.SafeDetail(OcrOperationalReason.Waiting, "  "));
    }
}
