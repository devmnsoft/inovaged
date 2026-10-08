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
        var repoFile = Path.Combine(AppContext.BaseDirectory, "../../../../../InovaGed.Infrastructure/Preview/PreviewStatusRepository.cs");
        if (File.Exists(repoFile))
        {
            var content = File.ReadAllText(repoFile);
            Assert.DoesNotContain("dynamic", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("PreviewStatusRow", content);
            Assert.Contains("ColumnMetadataRow", content);
            Assert.Contains("AS \"TenantId\"", content);
            Assert.Contains("AS \"VersionId\"", content);
            Assert.Contains("AS \"Status\"", content);
            Assert.Contains("AS \"Attempts\"", content);
            Assert.Contains("AS \"LastUpdatedAt\"", content);
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
        var migrationPath = Path.Combine(AppContext.BaseDirectory, "../../../../../database/migrations/2026_10_14_preview_status_upsert_contract.sql");
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
}
