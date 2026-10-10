using InovaGed.Application.Ocr;
using InovaGed.Infrastructure.Ocr;
using Xunit;

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

    [Fact]
    public void Operational_states_and_diagnostics_contracts_are_explicit_and_safe()
    {
        Assert.Equal("disponível com resultados", OcrOperationalState.AvailableWithResults);
        Assert.Equal("disponível sem resultados", OcrOperationalState.AvailableEmpty);
        Assert.Equal("indisponível", OcrOperationalState.Unavailable);
        Assert.Equal("acesso negado", OcrOperationalState.AccessDenied);

        var diag = new OcrDiagnosticInfo
        {
            ErrorCode = "ERR_OCR_DB_UNAVAILABLE",
            Stage = "QueryRunReasons",
            SqlState = "08006",
            CorrelationId = "corr-123",
            CanRetry = true,
            RecoveryAction = "Conexão ou tabela indisponível no banco de dados. Tente novamente após restabelecer conexão."
        };

        Assert.Equal("ERR_OCR_DB_UNAVAILABLE", diag.ErrorCode);
        Assert.Equal("QueryRunReasons", diag.Stage);
        Assert.Equal("08006", diag.SqlState);
        Assert.Equal("corr-123", diag.CorrelationId);
        Assert.True(diag.CanRetry);
        Assert.DoesNotContain("password", diag.RecoveryAction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Operational_reasons_query_result_paginates_and_distinguishes_historical_from_current()
    {
        var result = new OcrOperationalReasonsQueryResult
        {
            State = OcrOperationalState.AvailableWithResults,
            TotalCount = 45,
            Page = 2,
            PageSize = 20,
            WaitingCount = 10,
            ProcessingCount = 5,
            CompletedCount = 25,
            FailedCount = 3,
            NeedsInterventionCount = 2,
            Items = new List<OcrOperationalReasonDto>
            {
                new()
                {
                    DocumentId = Guid.NewGuid(),
                    VersionId = Guid.NewGuid(),
                    FileName = "doc1.pdf",
                    RunItemStatus = "QUEUED",
                    CurrentJobStatus = "PROCESSING",
                    Situation = OcrOperationalReason.Processing,
                    JobId = 42
                }
            }
        };

        Assert.Equal(3, result.TotalPages);
        Assert.Equal(45, result.TotalCount);
        Assert.Single(result.Items);
        var item = result.Items[0];
        Assert.Equal("QUEUED", item.RunItemStatus);
        Assert.Equal("PROCESSING", item.CurrentJobStatus);
        Assert.Equal(OcrOperationalReason.Processing, item.Situation);
        Assert.Equal(42L, item.JobId);
    }

    [Fact]
    public void Repository_query_includes_deterministic_tie_break_and_safe_diagnostics()
    {
        var repoFile = Path.Combine(AppContext.BaseDirectory, "../../../../../InovaGed.Infrastructure/Ocr/OcrAutoScheduleRepository.cs");
        if (File.Exists(repoFile))
        {
            var content = File.ReadAllText(repoFile);
            Assert.Contains("GetRunReasonsPagedAsync", content);
            Assert.Contains("OcrOperationalState.AccessDenied", content);
            Assert.Contains("OcrOperationalState.Unavailable", content);
            Assert.Contains("OcrOperationalState.AvailableWithResults", content);
            Assert.Contains("OcrOperationalState.AvailableEmpty", content);
            Assert.Contains("COALESCE(j.finished_at, j.requested_at) DESC NULLS LAST", content);
            Assert.Contains("j.id DESC", content);
            Assert.Contains("42501", content);
        }
    }
}
