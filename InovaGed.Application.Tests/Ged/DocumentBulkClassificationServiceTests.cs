using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Documents;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Retention;
using InovaGed.Application.Security;
using InovaGed.Domain.Primitives;
using InovaGed.Infrastructure.Ged.Documents;
using Xunit;

namespace InovaGed.Application.Tests.Ged;

public sealed class DocumentBulkClassificationServiceTests
{
    private sealed class FakeAuditWriter : IAuditWriter
    {
        public List<object?> Writes { get; } = new();

        public Task<Result> WriteAsync(AuditWriteCommand command, CancellationToken ct)
        {
            Writes.Add(command.Data);
            return Task.FromResult(Result.Ok());
        }

        public Task<Result> WriteAsync(Guid tenantId, Guid? userId, string action, string entityName, Guid? entityId, string? summary, string? ipAddress, string? userAgent, object? data, CancellationToken ct)
        {
            Writes.Add(data);
            return Task.FromResult(Result.Ok());
        }
    }

    private sealed class FakeCommands : IDocumentCommands
    {
        public List<Guid> Applied { get; } = new();

        public Task<Result> DeleteAsync(Guid tenantId, Guid documentId, Guid? userId, bool forceStopOcr, CancellationToken ct) =>
            Task.FromResult(Result.Ok());

        public Task ApplyClassificationAsync(Guid tenantId, Guid userId, Guid documentId, Guid classificationId, CancellationToken ct)
        {
            Applied.Add(documentId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRetention : IRetentionRecalcService
    {
        public bool ShouldFail { get; set; }
        public List<Guid> Recalculated { get; } = new();

        public Task<int> RunAsync(Guid tenantId, int dueSoonDays, CancellationToken ct) => Task.FromResult(0);

        public Task<int> RunOneAsync(Guid tenantId, Guid documentId, int dueSoonDays, CancellationToken ct)
        {
            if (ShouldFail) throw new InvalidOperationException("Falha simulada de recálculo");
            Recalculated.Add(documentId);
            return Task.FromResult(1);
        }
    }

    private sealed class FakeAbac : IAbacAuthorizationService
    {
        public HashSet<Guid> AllowedIds { get; } = new();

        public Task<bool> CanAccessDocumentAsync(Guid tenantId, Guid userId, Guid documentId, string action, IReadOnlyDictionary<string, string> attributes, CancellationToken ct)
            => Task.FromResult(AllowedIds.Contains(documentId));

        public Task<IReadOnlySet<Guid>> FilterDocumentsAsync(Guid tenantId, Guid userId, IReadOnlyCollection<Guid> documentIds, string action, CancellationToken ct)
        {
            IReadOnlySet<Guid> result = documentIds.Where(AllowedIds.Contains).ToHashSet();
            return Task.FromResult(result);
        }
    }

    [Fact]
    public async Task ApplyAsync_RejectsBatchOver500Documents()
    {
        var service = new DocumentBulkClassificationService(
            null!, new FakeCommands(), new FakeRetention(), new FakeAuditWriter(), new FakeAbac());

        var ids = Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToList();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ApplyAsync(Guid.NewGuid(), Guid.NewGuid(), ids, Guid.NewGuid(), CancellationToken.None));

        Assert.Contains("500", ex.Message);
    }

    [Fact]
    public async Task ApplyAsync_ReturnsEmptyResult_WhenNoDocumentsProvided()
    {
        var service = new DocumentBulkClassificationService(
            null!, new FakeCommands(), new FakeRetention(), new FakeAuditWriter(), new FakeAbac());

        var result = await service.ApplyAsync(Guid.NewGuid(), Guid.NewGuid(), Array.Empty<Guid>(), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(0, result.Requested);
        Assert.Equal(0, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task ApplyAsync_HonorsCancellation()
    {
        var service = new DocumentBulkClassificationService(
            null!, new FakeCommands(), new FakeRetention(), new FakeAuditWriter(), new FakeAbac());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ApplyAsync(Guid.NewGuid(), Guid.NewGuid(), new[] { Guid.NewGuid() }, Guid.NewGuid(), cts.Token));
    }
}
