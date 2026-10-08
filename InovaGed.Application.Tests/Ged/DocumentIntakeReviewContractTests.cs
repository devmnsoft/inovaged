using System.Data;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Documents;
using InovaGed.Domain.Primitives;
using InovaGed.Infrastructure.Ged.Documents;
using Npgsql;
using Xunit;

namespace InovaGed.Application.Tests.Ged;

public class DocumentIntakeReviewContractTests
{
    [Fact]
    public void DocumentIntakeReviewService_SqlAliases_DoNotContainEscapedBackslashes()
    {
        // Assert that raw SQL strings do not have \" inside them causing Postgres 42601 syntax errors
        var serviceFile = Path.Combine(AppContext.BaseDirectory, "../../../../InovaGed.Infrastructure/Ged/Documents/DocumentIntakeReviewService.cs");
        if (File.Exists(serviceFile))
        {
            var content = File.ReadAllText(serviceFile);
            Assert.DoesNotContain("\\\"DocumentId\\\"", content);
            Assert.DoesNotContain("\\\"Status\\\"", content);
            Assert.DoesNotContain("\\\"ReviewedBy\\\"", content);
            Assert.DoesNotContain("\\\"ReviewedAt\\\"", content);
            Assert.DoesNotContain("\\\"Notes\\\"", content);
            Assert.Contains("\"DocumentId\"", content);
            Assert.Contains("\"Status\"", content);
        }
    }

    [Fact]
    public void DocumentIntakeReviewStatus_Constants_AreValid()
    {
        Assert.Equal("PENDING", DocumentIntakeReviewStatus.Pending);
        Assert.Equal("REVIEWED", DocumentIntakeReviewStatus.Reviewed);
        Assert.Equal("NEEDS_CORRECTION", DocumentIntakeReviewStatus.NeedsCorrection);
    }

    [Fact]
    public async Task GetForDocumentsAsync_WhenDocumentIdsIsEmpty_ReturnsEmptyDictionaryWithoutDbCall()
    {
        var service = new DocumentIntakeReviewService(new FakeDbConnectionFactory(), new FakeAuditWriter());
        var result = await service.GetForDocumentsAsync(Guid.NewGuid(), Array.Empty<Guid>(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    private sealed class FakeDbConnectionFactory : IDbConnectionFactory
    {
        public IDbConnection CreateConnection() => throw new NotImplementedException();
        public Task<NpgsqlConnection> OpenAsync(CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class FakeAuditWriter : IAuditWriter
    {
        public Task<Result> WriteAsync(Guid tenantId, Guid? userId, string action, string entityName, Guid? entityId, string? summary, string? ipAddress, string? userAgent, object? data, CancellationToken ct) => Task.FromResult(Result.Ok());
        public Task<Result> WriteAsync(AuditWriteCommand command, CancellationToken ct) => Task.FromResult(Result.Ok());
    }
}
