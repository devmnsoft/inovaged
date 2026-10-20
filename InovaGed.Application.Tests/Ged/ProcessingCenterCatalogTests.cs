using InovaGed.Application.Ged.Processing;
using Xunit;

namespace InovaGed.Application.Tests.Ged;

public sealed class ProcessingCenterCatalogTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Normalize_MapsDashboardErrorLinkAndClampsTheWindow()
    {
        var criteria = ProcessingCenterCatalog.Normalize(
            "error",
            "ocr",
            "  %contrato_  ",
            new DateTime(2020, 1, 1),
            new DateTime(2026, 10, 16),
            "not-a-guid",
            Guid.Empty.ToString(),
            Now);

        Assert.Equal("failed", criteria.StatusGroup);
        Assert.Equal("OCR", criteria.Lane);
        Assert.Equal("%contrato%", criteria.NameLike);
        Assert.Null(criteria.UserId);
        Assert.Null(criteria.DocumentTypeId);
        Assert.Contains("ERROR", criteria.Statuses);
        Assert.True(criteria.To - criteria.From <= TimeSpan.FromDays(ProcessingCenterCatalog.MaxWindowDays));
    }

    [Theory]
    [InlineData("FAILED", 3, 3, "Falha definitiva")]
    [InlineData("ERROR", 1, 3, "Falha recuperável")]
    [InlineData("PENDING", 0, 3, "Aguardando")]
    [InlineData("PROCESSING", 1, 3, "Em processamento")]
    [InlineData("COMPLETED", 1, 3, "Concluído")]
    [InlineData("CANCELLED", 1, 3, "Cancelado")]
    public void Headline_UsesOperationalLanguage(string status, int attempts, int maxAttempts, string expected)
    {
        var headline = ProcessingCenterCatalog.Headline(status, attempts, maxAttempts);
        Assert.Equal(expected, headline);
        Assert.DoesNotContain(status, headline, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Actions_ReprocessFailuresAndDoNotCancelUploadsFromThisScreen()
    {
        Assert.True(ProcessingCenterCatalog.CanReprocess("JOB", "FAILED"));
        Assert.True(ProcessingCenterCatalog.CanReprocess("OCR", "ERROR"));
        Assert.False(ProcessingCenterCatalog.CanReprocess("JOB", "COMPLETED"));
        Assert.False(ProcessingCenterCatalog.CanReprocess("UPLOAD", "ERROR"));
        Assert.False(ProcessingCenterCatalog.CanCancel("UPLOAD", "RECEIVING"));
        Assert.True(ProcessingCenterCatalog.CanCancel("JOB", "PROCESSING"));
        Assert.False(ProcessingCenterCatalog.CanCancel("OCR", "COMPLETED"));
        Assert.Equal(20, ProcessingCenterCatalog.MaxBatchReprocess);
        Assert.Contains("target.id = ANY(@jobIds)", ProcessingCenterSql.RetryJobs, StringComparison.Ordinal);
        Assert.Contains("target.tenant_id = @tenantId", ProcessingCenterSql.RetryJobs, StringComparison.Ordinal);
    }

    [Fact]
    public void Timeline_skips_conversion_for_pdf_and_marks_office_conversion_failure()
    {
        var pdf = ProcessingCenterCatalog.Timeline("OCR", "COMPLETED", "laudo.pdf", null);
        Assert.Equal("Dispensada", pdf[1].State);
        Assert.Equal("Concluída", pdf[2].State);
        Assert.Equal("Concluída", pdf[4].State);

        var office = ProcessingCenterCatalog.Timeline("OCR", "ERROR", "guia clínica (1).docx", InovaGed.Application.Common.Preview.DocumentConversionException.TimeoutMessage);
        Assert.Equal("Falhou", office[1].State);
        Assert.Equal("Aguardando", office[2].State);
        Assert.DoesNotContain(office, stage => stage.State == "Falhou" && stage.Name == "OCR");

        var preview = ProcessingCenterCatalog.Timeline("PREVIEW", "READY", "planilha.xlsx", null);
        Assert.Equal("Concluída", preview[1].State);
        Assert.Equal("Dispensada", preview[2].State);
    }

    [Fact]
    public void Sql_KeepsTenantPredicateAndDoesNotCompareEnumAsVarchar()
    {
        Assert.Contains("j.tenant_id = @tenantId", ProcessingCenterSql.Jobs, StringComparison.Ordinal);
        Assert.Contains("j.tenant_id = @tenantId", ProcessingCenterSql.Ocr, StringComparison.Ordinal);
        Assert.Contains("s.tenant_id = @tenantId", ProcessingCenterSql.Uploads, StringComparison.Ordinal);
        Assert.Contains("j.status::text = ANY(@statuses)", ProcessingCenterSql.Ocr, StringComparison.Ordinal);
        Assert.Contains("target.tenant_id = @tenantId", ProcessingCenterSql.RetryJob, StringComparison.Ordinal);
        Assert.DoesNotContain("j.status = @status", ProcessingCenterSql.Ocr, StringComparison.Ordinal);
        Assert.Contains("NOT EXISTS", ProcessingCenterSql.RetryJob, StringComparison.Ordinal);
        Assert.Contains("AverageProcessingSeconds", ProcessingCenterSql.JobCounts, StringComparison.Ordinal);

        var worker = File.ReadAllText(Path.Combine(RepoRoot(), "InovaGed.Infrastructure/Ged/Documents/GedProcessingWorker.cs"));
        Assert.Contains("RecoverAbandonedExceptAsync(\"PREVIEW\"", worker, StringComparison.Ordinal);
        Assert.Contains("TouchLockAsync", worker, StringComparison.Ordinal);
        Assert.DoesNotContain("Worker cancelado", worker, StringComparison.Ordinal);

        var controller = File.ReadAllText(Path.Combine(RepoRoot(), "InovaGed.Web/Controllers/OcrController.cs"));
        Assert.DoesNotContain("[HttpGet(\"/Ged/Processing\")]", controller, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "InovaGed.sln")))
            current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Raiz do repositório não encontrada.");
    }
}
