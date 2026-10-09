using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Documents;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Retention;
using InovaGed.Application.Security;
using InovaGed.Domain.Primitives;
using InovaGed.Infrastructure.Ged.Documents;
using Dapper;
using InovaGed.Infrastructure.Common.Database;
using InovaGed.Infrastructure.Retention;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
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

    private sealed class ToggleRetentionJobs : IRetentionJobRepository
    {
        private readonly IRetentionJobRepository _inner;

        public ToggleRetentionJobs(IRetentionJobRepository inner)
        {
            _inner = inner;
        }

        public bool ShouldFail { get; set; }

        public Task<int> RecalculateAsync(Guid tenantId, int dueSoonDays, CancellationToken ct) =>
            _inner.RecalculateAsync(tenantId, dueSoonDays, ct);

        public Task<RetentionDashboardVM> GetDashboardAsync(Guid tenantId, int dueSoonDays, CancellationToken ct) =>
            _inner.GetDashboardAsync(tenantId, dueSoonDays, ct);

        public Task<int> RecalculateOneAsync(Guid tenantId, Guid documentId, int dueSoonDays, CancellationToken ct) =>
            ShouldFail
                ? throw new InvalidOperationException("Falha simulada de recálculo")
                : _inner.RecalculateOneAsync(tenantId, documentId, dueSoonDays, ct);

        public Task<int> RecalculateOneAsync(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, Guid tenantId, Guid documentId, int dueSoonDays, CancellationToken ct) =>
            ShouldFail
                ? throw new InvalidOperationException("Falha simulada de recálculo")
                : _inner.RecalculateOneAsync(connection, transaction, tenantId, documentId, dueSoonDays, ct);

        public Task<Guid> EnqueueRecalculateAsync(Guid tenantId, Guid documentId, string reason, CancellationToken ct) =>
            _inner.EnqueueRecalculateAsync(tenantId, documentId, reason, ct);

        public Task<Guid> EnqueueRecalculateAsync(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, Guid tenantId, Guid documentId, string reason, CancellationToken ct) =>
            _inner.EnqueueRecalculateAsync(connection, transaction, tenantId, documentId, reason, ct);

        public Task<bool> ResolvePendingRecalcAsync(Guid tenantId, Guid pendingId, CancellationToken ct) =>
            _inner.ResolvePendingRecalcAsync(tenantId, pendingId, ct);

        public Task<bool> ResolvePendingRecalcAsync(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, Guid tenantId, Guid pendingId, CancellationToken ct) =>
            _inner.ResolvePendingRecalcAsync(connection, transaction, tenantId, pendingId, ct);
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
            null!, new FakeCommands(), new FakeRetention(), null!, null!, new FakeAuditWriter(), new FakeAbac());

        var ids = Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToList();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ApplyAsync(Guid.NewGuid(), Guid.NewGuid(), ids, Guid.NewGuid(), CancellationToken.None));

        Assert.Contains("500", ex.Message);
    }

    [Fact]
    public async Task ApplyAsync_ReturnsEmptyResult_WhenNoDocumentsProvided()
    {
        var service = new DocumentBulkClassificationService(
            null!, new FakeCommands(), new FakeRetention(), null!, null!, new FakeAuditWriter(), new FakeAbac());

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
            null!, new FakeCommands(), new FakeRetention(), null!, null!, new FakeAuditWriter(), new FakeAbac());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ApplyAsync(Guid.NewGuid(), Guid.NewGuid(), new[] { Guid.NewGuid() }, Guid.NewGuid(), cts.Token));
    }

    [Fact]
    public async Task Result_counts_distinguish_applied_pending_denied_and_failed()
    {
        var items = new DocumentBulkClassificationItem[]
        {
            new(Guid.NewGuid(), true, "", "APPLIED", "APPLIED"),
            new(Guid.NewGuid(), true, "", "PENDING", "RETENTION_PENDING"),
            new(Guid.NewGuid(), false, "", "DENIED", "NOT_FOUND"),
            new(Guid.NewGuid(), false, "", "FAILED", "FAILED"),
        };
        var r = new DocumentBulkClassificationResult(4, 2, 1, items);
        Assert.Equal((1, 1, 1, 1), (r.Applied, r.Pending, r.Denied, r.Failed));
        await Task.CompletedTask;
    }

    [PgGatedFact]
    public async Task Pg_pending_is_committed_with_classification_and_failures_are_sanitized()
    {
        await using var admin = new NpgsqlConnection(PgGate.Dsn());
        await admin.OpenAsync();
        await admin.ExecuteAsync(BulkSchema);
        var tenant = Guid.NewGuid(); var user = Guid.NewGuid(); var doc = Guid.NewGuid(); var denied = Guid.NewGuid();
        var cls = Guid.NewGuid(); var plan = Guid.NewGuid();
        await admin.ExecuteAsync("""
insert into ged.tenant(id,name,code) values(@tenant,'bulk',@tenant::text);
insert into ged.document(id,tenant_id,title,reg_status) values(@doc,@tenant,'a','A'),(@denied,@tenant,'b','A');
insert into ged.classification_plan(id,tenant_id,code,name) values(@cls,@tenant,'C','C');
insert into ged.classification_plan_version(id,tenant_id,version_no,title) values(@plan,@tenant,1,'V1');
insert into ged.classification_plan_version_item(tenant_id,version_id,classification_id,code,name,is_active) values(@tenant,@plan,@cls,'1','C',true);
""", new { tenant, doc, denied, cls, plan });

        var abac = new FakeAbac(); abac.AllowedIds.Add(doc);
        var factory = new NpgsqlConnectionFactory(PgGate.Dsn());
        var retentionJobs = new ToggleRetentionJobs(new RetentionJobRepository(factory, NullLogger<RetentionJobRepository>.Instance))
        {
            ShouldFail = true
        };
        var retention = new FakeRetention();
        var recovery = new AssistedRetentionRecovery(factory, retentionJobs);
        var service = new DocumentBulkClassificationService(factory, new FakeCommands(), retention, retentionJobs, recovery, new FakeAuditWriter(), abac);
        var result = await service.ApplyAsync(tenant, user, new[] { doc, doc, denied, Guid.Empty }, cls, CancellationToken.None);

        Assert.Equal(new[] { "PENDING", "DENIED", "DENIED", "DENIED" }, result.Items.Select(x => x.Status));
        Assert.Equal(new[] { "RETENTION_PENDING", "DUPLICATE_ID", "NOT_FOUND", "INVALID_ID" }, result.Items.Select(x => x.Code));
        Assert.DoesNotContain("simulada", string.Join(' ', result.Items.Select(x => x.Message)));
        Assert.Equal(cls, await admin.ExecuteScalarAsync<Guid>("select classification_id from ged.document where id=@doc", new { doc }));
        Assert.Null(await admin.ExecuteScalarAsync<Guid?>("select classification_id from ged.document where id=@denied", new { denied }));
        Assert.Equal(1, await admin.ExecuteScalarAsync<int>("select count(*) from ged.ai_retention_recalc_pending where document_id=@doc and application_id is null and resolved_at is null", new { doc }));

        retentionJobs.ShouldFail = false;
        var again = await service.ApplyAsync(tenant, user, new[] { doc }, cls, CancellationToken.None);
        Assert.Equal("APPLIED", again.Items[0].Status);
        Assert.Equal(0, await admin.ExecuteScalarAsync<int>("select count(*) from ged.ai_retention_recalc_pending where document_id=@doc and resolved_at is null", new { doc }));
    }

    private const string BulkSchema = """
create schema if not exists ged;
create extension if not exists pgcrypto;
create table if not exists ged.tenant(id uuid primary key, name text, code varchar(50));
create table if not exists ged.document(id uuid primary key, tenant_id uuid not null, title text, classification_id uuid, classification_version_id uuid,
  updated_at timestamptz, updated_by uuid, reg_status char(1) not null default 'A', status text not null default 'ACTIVE');
alter table ged.document add column if not exists classification_id uuid;
alter table ged.document add column if not exists classification_version_id uuid;
alter table ged.document add column if not exists updated_at timestamptz;
alter table ged.document add column if not exists updated_by uuid;
create table if not exists ged.classification_plan(id uuid primary key, tenant_id uuid not null, code text, name text);
create table if not exists ged.classification_plan_version(id uuid primary key, tenant_id uuid not null, version_no int not null, title text);
create table if not exists ged.classification_plan_version_item(tenant_id uuid not null, version_id uuid not null, classification_id uuid not null, code text, name text, is_active boolean not null default true);
create table if not exists ged.document_classification(id uuid primary key default gen_random_uuid(), tenant_id uuid not null, document_id uuid not null,
  document_version_id uuid, classification_id uuid, classification_version_id uuid, confidence numeric(5,4), method text, summary text,
  source text, classified_by uuid, classified_at timestamptz not null default now(), updated_at timestamptz, reg_status char(1) not null default 'A');
create unique index if not exists ux_test_document_classification_document on ged.document_classification(document_id);
create table if not exists ged.ai_retention_recalc_pending(id uuid primary key, tenant_id uuid not null, document_id uuid not null, application_id uuid null,
  reason varchar(500) not null, created_at timestamptz not null default now(), resolved_at timestamptz null, attempts integer not null default 0,
  last_error varchar(200), claimed_at timestamptz, claim_token uuid, next_attempt_at timestamptz not null default now());
""";
}
