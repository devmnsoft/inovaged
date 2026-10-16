using System.Reflection;
using InovaGed.Application.Preview;
using InovaGed.Infrastructure.Preview;
using Xunit;

namespace InovaGed.Application.Tests.Ged;

public sealed class PreviewStatusContractAndRepositoryTests
{
    [Fact]
    public void PreviewStatusRepository_DoesNotUseDynamicInGetAsync()
    {
        var repoType = typeof(PreviewStatusRepository);
        var getAsyncMethod = repoType.GetMethod(nameof(PreviewStatusRepository.GetAsync));

        Assert.NotNull(getAsyncMethod);

        // Verify that PreviewStatusRepository does not expose or rely on dynamic binding
        var repoFile = Path.Combine(RepoRoot(), "InovaGed.Infrastructure/Preview/PreviewStatusRepository.cs");
        if (File.Exists(repoFile))
        {
            var content = File.ReadAllText(repoFile);
            Assert.DoesNotContain("dynamic", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("PreviewStatusRow", content);
            Assert.Contains("ColumnMetadataRow", content);
            Assert.Contains("AS \"\"TenantId\"\"", content);
            Assert.Contains("AS \"\"VersionId\"\"", content);
            Assert.Contains("AS \"\"Status\"\"", content);
            Assert.Contains("AS \"\"Attempts\"\"", content);
            Assert.Contains("AS \"\"LastUpdatedAt\"\"", content);
            Assert.Contains("effectiveRequestedAt", content);
            Assert.Contains("requested_at = COALESCE(ged.preview_status.requested_at, EXCLUDED.requested_at, now())", content);
            Assert.Contains("ged.preview_status.current_attempt_id IS DISTINCT FROM EXCLUDED.current_attempt_id", content);
            Assert.Contains("CAST(@statusText AS ged.preview_processing_status)", content);
            Assert.Contains("CAST(@statusText AS text) = 'PROCESSING'", content);
            Assert.DoesNotContain("@status::ged.preview_processing_status", content);
            Assert.DoesNotContain("@status = 'PROCESSING'", content);
            Assert.DoesNotContain("WHEN EXCLUDED.status IN ('PROCESSING'::ged.preview_processing_status, 'FAILED'::ged.preview_processing_status) THEN 1", content);
        }
    }

    [Theory]
    [InlineData("READY", PreviewProcessingStatus.Ready)]
    [InlineData("PROCESSING", PreviewProcessingStatus.Processing)]
    [InlineData("FAILED", PreviewProcessingStatus.Error)]
    [InlineData("ERROR", PreviewProcessingStatus.Error)]
    [InlineData("CANCELED", PreviewProcessingStatus.Canceled)]
    [InlineData("CANCELLED", PreviewProcessingStatus.Canceled)]
    [InlineData("PENDING", PreviewProcessingStatus.Pending)]
    [InlineData("UNKNOWN", PreviewProcessingStatus.Pending)]
    [InlineData(null, PreviewProcessingStatus.Pending)]
    public void PreviewStatus_StatusParsing_MapsCorrectly(string? statusText, PreviewProcessingStatus expected)
    {
        var repoType = typeof(PreviewStatusRepository);
        var parseMethod = repoType.GetMethod("ParseStatus", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(parseMethod);

        var result = (PreviewProcessingStatus)parseMethod.Invoke(null, new object?[] { statusText })!;
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(PreviewProcessingStatus.Ready, "READY")]
    [InlineData(PreviewProcessingStatus.Processing, "PROCESSING")]
    [InlineData(PreviewProcessingStatus.Error, "FAILED")]
    [InlineData(PreviewProcessingStatus.Canceled, "CANCELED")]
    [InlineData(PreviewProcessingStatus.Pending, "PENDING")]
    public void PreviewStatus_ToDatabaseStatus_MapsCorrectly(PreviewProcessingStatus status, string expected)
    {
        var repoType = typeof(PreviewStatusRepository);
        var toDbMethod = repoType.GetMethod("ToDatabaseStatus", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(toDbMethod);

        var result = (string)toDbMethod.Invoke(null, new object[] { status })!;
        Assert.Equal(expected, result);
    }

    [Fact]
    public void PreviewStatusDto_PreservesAllRequiredFields()
    {
        var tenantId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var dto = new PreviewStatusDto
        {
            TenantId = tenantId,
            VersionId = versionId,
            Status = PreviewProcessingStatus.Ready,
            PreviewPath = "previews/tenant/doc/ver.pdf",
            ErrorMessage = null,
            Attempts = 3,
            LastUpdatedAt = now,
            RequestedAt = now.AddMinutes(-2),
            FinishedAt = now,
            LastAttemptAt = now.AddMinutes(-1)
        };

        Assert.Equal(tenantId, dto.TenantId);
        Assert.Equal(versionId, dto.VersionId);
        Assert.Equal(PreviewProcessingStatus.Ready, dto.Status);
        Assert.Equal("previews/tenant/doc/ver.pdf", dto.PreviewPath);
        Assert.Null(dto.ErrorMessage);
        Assert.Equal(3, dto.Attempts);
        Assert.Equal(now, dto.LastUpdatedAt);
        Assert.Equal(now.AddMinutes(-2), dto.RequestedAt);
        Assert.Equal(now, dto.FinishedAt);
        Assert.Equal(now.AddMinutes(-1), dto.LastAttemptAt);
    }

    [Fact]
    public void MigrationFile_PreviewStatusUpsertContract_ContainsRequiredElements()
    {
        var migrationPath = Path.Combine(RepoRoot(), "database/migrations/2026_10_14_preview_status_upsert_contract.sql");
        if (File.Exists(migrationPath))
        {
            var sql = File.ReadAllText(migrationPath);
            Assert.Contains("ged.preview_status", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("tenant_id", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("document_version_id", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ux_preview_status_tenant_version", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("attempts", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("last_attempt_at", sql, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void PreviewStatusRequestedAtContract_IsRequiredMigration()
    {
        var migrationPath = Path.Combine(RepoRoot(), "database/migrations/2026_10_23_preview_status_requested_at_contract.sql");
        Assert.True(File.Exists(migrationPath));

        var sql = File.ReadAllText(migrationPath);
        Assert.Contains("ALTER COLUMN requested_at SET DEFAULT now()", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ALTER COLUMN requested_at SET NOT NULL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ux_preview_status_tenant_version", sql, StringComparison.OrdinalIgnoreCase);

        var requiredPath = Path.Combine(RepoRoot(), "database/required_migrations.json");
        var required = File.ReadAllText(requiredPath);
        Assert.Contains("2026_10_23_preview_status_requested_at_contract", required, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PreviewAttemptMigration_DistinguishesProvenAndReconstructedRequestTime()
    {
        var migrationPath = Path.Combine(RepoRoot(), "database/migrations/2026_10_24_preview_attempt_and_recovery.sql");
        Assert.True(File.Exists(migrationPath));
        var sql = File.ReadAllText(migrationPath);
        Assert.Contains("requested_at_kind", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'RECONSTRUCTED'", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'UNSPECIFIED'", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("current_attempt_id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("attempt_started_at", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("outcome_confirmed_at", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ged.processing_job", sql, StringComparison.OrdinalIgnoreCase);

        var requiredPath = Path.Combine(RepoRoot(), "database/required_migrations.json");
        Assert.Contains("2026_10_24_preview_attempt_and_recovery", File.ReadAllText(requiredPath), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PreviewEnumOperatorMigration_SeparatesTextFromEnumAndKeepsRows()
    {
        var migrationPath = Path.Combine(RepoRoot(), "database/migrations/2026_11_07_preview_status_enum_operator_contract.sql");
        Assert.True(File.Exists(migrationPath));
        var sql = File.ReadAllText(migrationPath);
        Assert.Contains("preview_processing_status", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ALTER COLUMN status TYPE ged.preview_processing_status", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ux_preview_status_tenant_version", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Nenhum dado foi alterado", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("delete from ged.preview_status", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("USING status::text", sql, StringComparison.OrdinalIgnoreCase);

        var required = File.ReadAllText(Path.Combine(RepoRoot(), "database/required_migrations.json"));
        Assert.Contains("2026_11_07_preview_status_enum_operator_contract", required, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IndexedSearchAndBoxLabel_UseLiveClassNotPlanOrStoredIndex()
    {
        var search = File.ReadAllText(Path.Combine(RepoRoot(), "InovaGed.Infrastructure/Ged/Search/GedSmartSearchService.cs"));
        Assert.DoesNotContain("si.classification", search, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cp.name, cp.code, pvi.name, pvi.code", search, StringComparison.Ordinal);
        Assert.Contains("ArchivalClassificationSql.Name()", search, StringComparison.Ordinal);
        Assert.Contains("cp.id=d.classification_id", search, StringComparison.Ordinal);

        var labels = File.ReadAllText(Path.Combine(RepoRoot(), "InovaGed.Web/Controller/LabelsController.cs"));
        Assert.DoesNotContain("count(distinct cp.id)", labels, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("count(distinct ({ArchivalClassificationSql.Label()}))", labels, StringComparison.Ordinal);
    }

    [Fact]
    public void RetentionSql_UsesClassNameAndKeepsPlanTitleSeparate()
    {
        var path = Path.Combine(RepoRoot(), "InovaGed.Infrastructure/Retention/RetentionJobRepository.cs");
        var content = File.ReadAllText(path);
        Assert.Contains("ArchivalClassificationSql.Name(\"d\", \"c\", \"pvi\")", content, StringComparison.Ordinal);
        Assert.Contains("pv.title as plan_version_title", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pvi.title", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PreviewPipeline_ReusesDurableProcessingJobAndSeparatesPersistence()
    {
        var root = Path.Combine(RepoRoot(), "InovaGed.Infrastructure/Preview");
        var worker = File.ReadAllText(Path.Combine(root, "PreviewWorker.cs"));
        var queue = File.ReadAllText(Path.Combine(root, "PreviewQueue.cs"));
        var processor = File.ReadAllText(Path.Combine(root, "PreviewJobProcessor.cs"));
        var generator = File.ReadAllText(Path.Combine(RepoRoot(), "InovaGed.Application/Common/Preview/LibreOfficePreviewGenerator.cs"));
        var processingWorker = File.ReadAllText(Path.Combine(RepoRoot(), "InovaGed.Infrastructure/Ged/Documents/GedProcessingWorker.cs"));

        Assert.Contains("ged.processing_job", queue, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CreateUnbounded", queue, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DequeueAsync(_workerId, 1, \"PREVIEW\"", worker, StringComparison.Ordinal);
        Assert.Contains("RecoverAbandonedAsync", worker, StringComparison.Ordinal);
        Assert.Contains("PersistencePending", processor, StringComparison.Ordinal);
        Assert.Contains("FailureUnrecorded", processor, StringComparison.Ordinal);
        Assert.Contains("FAILURE_PERSIST", processor, StringComparison.Ordinal);
        Assert.Contains("SupportCode={SupportCode}", processor, StringComparison.Ordinal);
        Assert.Contains("CycleBackoff", worker, StringComparison.Ordinal);
        Assert.DoesNotContain("BackgroundServiceExceptionBehavior", worker, StringComparison.Ordinal);
        Assert.Contains("ConversionFailed", processor, StringComparison.Ordinal);
        Assert.Contains("NotificationFailed", processor, StringComparison.Ordinal);
        Assert.Contains("ExistsAsync(previewRelPath", generator, StringComparison.Ordinal);
        Assert.Contains("ReleaseAsync", processingWorker, StringComparison.Ordinal);
        Assert.DoesNotContain("case \"PREVIEW\":\r\n                    break;", processingWorker, StringComparison.Ordinal);
        Assert.DoesNotContain("case \"PREVIEW\":\n                    break;", processingWorker, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeSql_DoesNotReferenceLegacyClassificationVersionItemTitle()
    {
        var root = RepoRoot();
        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}InovaGed.", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}InovaGed.Application.Tests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            Assert.DoesNotContain("pvi.title", content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void AtlasViews_DoNotUseMissingClockIcon()
    {
        var viewsRoot = Path.Combine(RepoRoot(), "InovaGed.Web/Views");
        var files = Directory.EnumerateFiles(viewsRoot, "*.cshtml", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            Assert.DoesNotContain("name=\"clock\"", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"clock\"", content, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "InovaGed.sln")))
            current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Raiz do repositório não encontrada.");
    }
}
