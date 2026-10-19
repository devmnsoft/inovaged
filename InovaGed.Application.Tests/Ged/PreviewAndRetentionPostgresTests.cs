using Dapper;
using InovaGed.Application.Classification;
using InovaGed.Application.Ged.Processing;
using InovaGed.Application.Preview;
using InovaGed.Infrastructure.Common.Database;
using InovaGed.Infrastructure.Ged.Documents;
using InovaGed.Infrastructure.Preview;
using InovaGed.Infrastructure.Retention;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace InovaGed.Application.Tests.Ged;

public sealed class LocalPgFactAttribute : FactAttribute
{
    public LocalPgFactAttribute()
    {
        if (LocalPg.Unavailable is not null)
            Skip = LocalPg.Unavailable;
    }
}

internal static class LocalPg
{
    private static readonly Lazy<string?> Probe = new(ProbeCore);
    public static string? Unavailable => Probe.Value;
    public static string AdminDsn { get; private set; } = string.Empty;

    private static string? ProbeCore()
    {
        foreach (var candidate in new[]
        {
            System.Environment.GetEnvironmentVariable("INOVAGED_PG_DSN"),
            System.Environment.GetEnvironmentVariable("INOVAGED_AI_PG_DSN"),
            "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=123456;Timeout=5;Command Timeout=60",
            "Host=localhost;Port=5432;Database=postgres;Username=postgres;Timeout=5;Command Timeout=60"
        })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            try
            {
                using var connection = new NpgsqlConnection(candidate);
                connection.Open();
                AdminDsn = candidate;
                return null;
            }
            catch
            {
            }
        }

        return "PostgreSQL local indisponível. Defina INOVAGED_PG_DSN para homologar preview e temporalidade.";
    }
}

public sealed class PreviewAndRetentionPostgresTests
{
    [LocalPgFact]
    public async Task Preview_attempt_requested_at_and_restart_survive_in_postgresql()
    {
        await using var database = await ScratchDatabase.CreateAsync();
        await database.ExecuteAsync("create schema if not exists ged;");
        await database.ExecuteAsync("""
create table if not exists ged.processing_job (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    document_id uuid null,
    document_version_id uuid null,
    upload_batch_id uuid null,
    upload_batch_item_id uuid null,
    job_type text not null,
    status text not null default 'PENDING',
    priority int not null default 5,
    attempt_count int not null default 0,
    max_attempts int not null default 3,
    error_message text null,
    started_at timestamptz null,
    finished_at timestamptz null,
    next_attempt_at timestamptz null,
    locked_by text null,
    locked_at timestamptz null,
    created_at timestamptz not null default now(),
    updated_at timestamptz null,
    reg_status char(1) not null default 'A'
);
""");
        await database.ExecuteFileAsync("database/migrations/2026_10_14_preview_status_upsert_contract.sql");
        await database.ExecuteFileAsync("database/migrations/2026_10_23_preview_status_requested_at_contract.sql");
        var historicalRequestedAt = DateTimeOffset.Parse("2024-05-01T12:00:00Z");
        var historicalVersion = Guid.NewGuid();
        await database.Connection.ExecuteAsync("""
insert into ged.preview_status (tenant_id, document_version_id, status, requested_at, last_attempt_at, attempts)
values (@tenant, @version, 'FAILED', @requestedAt, @requestedAt, 2);
""", new { tenant = database.TenantId, version = historicalVersion, requestedAt = historicalRequestedAt });
        await database.ExecuteFileAsync("database/migrations/2026_10_24_preview_attempt_and_recovery.sql");
        await database.ExecuteAsync("""
create unique index if not exists ux_processing_job_active_dedup_idx
on ged.processing_job (tenant_id, (coalesce(document_version_id, '00000000-0000-0000-0000-000000000000'::uuid)), (coalesce(upload_batch_item_id, '00000000-0000-0000-0000-000000000000'::uuid)), job_type)
where status in ('PENDING','PROCESSING') and reg_status='A';
""");

        var historicalKind = await database.Connection.ExecuteScalarAsync<string>("select requested_at_kind from ged.preview_status where document_version_id=@version", new { version = historicalVersion });
        Assert.Equal("RECONSTRUCTED", historicalKind);

        var repo = new PreviewStatusRepository(database.Factory, NullLogger<PreviewStatusRepository>.Instance);
        var versionId = Guid.NewGuid();
        var requestedAt = DateTimeOffset.Parse("2026-10-12T15:04:00Z");
        var attemptId = Guid.NewGuid();
        await repo.UpsertAsync(database.TenantId, versionId, PreviewProcessingStatus.Pending, null, null, requestedAt, null, CancellationToken.None);
        await repo.UpsertAsync(Write(database.TenantId, versionId, PreviewProcessingStatus.Processing, requestedAt, attemptId), CancellationToken.None);
        await repo.UpsertAsync(Write(database.TenantId, versionId, PreviewProcessingStatus.Processing, requestedAt, attemptId), CancellationToken.None);
        await repo.UpsertAsync(Write(database.TenantId, versionId, PreviewProcessingStatus.Error, requestedAt, attemptId, error: "conversão"), CancellationToken.None);
        await repo.UpsertAsync(Write(database.TenantId, versionId, PreviewProcessingStatus.Error, requestedAt, attemptId, error: "conversão"), CancellationToken.None);

        var failed = await repo.GetAsync(database.TenantId, versionId, CancellationToken.None);
        Assert.NotNull(failed);
        Assert.Equal(1, failed.Attempts);
        Assert.Equal(attemptId, failed.CurrentAttemptId);
        Assert.Equal(requestedAt, failed.RequestedAt);
        Assert.Equal("ORIGINAL", failed.RequestedAtKind);
        Assert.Equal(PreviewProcessingStatus.Error, failed.Status);
        Assert.NotNull(failed.FinishedAt);
        Assert.NotNull(failed.AttemptStartedAt);
        Assert.Null(failed.PreviewPath);

        var secondAttempt = Guid.NewGuid();
        await repo.UpsertAsync(Write(database.TenantId, versionId, PreviewProcessingStatus.Processing, DateTimeOffset.UtcNow, secondAttempt), CancellationToken.None);
        await repo.UpsertAsync(Write(database.TenantId, versionId, PreviewProcessingStatus.Ready, requestedAt, secondAttempt, path: "previews/classe.pdf", confirm: true), CancellationToken.None);
        var ready = await repo.GetAsync(database.TenantId, versionId, CancellationToken.None);
        Assert.NotNull(ready);
        Assert.Equal(2, ready.Attempts);
        Assert.Equal(requestedAt, ready.RequestedAt);
        Assert.Equal("ORIGINAL", ready.RequestedAtKind);
        Assert.Equal(PreviewProcessingStatus.Ready, ready.Status);
        Assert.Equal("previews/classe.pdf", ready.PreviewPath);
        Assert.Null(ready.ErrorMessage);
        Assert.NotNull(ready.OutcomeConfirmedAt);
        Assert.Equal(1, await database.Connection.ExecuteScalarAsync<int>("select count(*) from ged.preview_status where tenant_id=@tenant and document_version_id=@version", new { tenant = database.TenantId, version = versionId }));

        var observedVersion = Guid.NewGuid();
        await repo.UpsertAsync(new PreviewStatusUpsert { TenantId = database.TenantId, VersionId = observedVersion, Status = PreviewProcessingStatus.Pending }, CancellationToken.None);
        var observed = await repo.GetAsync(database.TenantId, observedVersion, CancellationToken.None);
        Assert.Equal("FIRST_OBSERVATION", observed!.RequestedAtKind);
        Assert.NotNull(observed.RequestedAt);

        var nullInsert = await Assert.ThrowsAsync<PostgresException>(() => database.Connection.ExecuteAsync("""
insert into ged.preview_status (tenant_id, document_version_id, status, requested_at)
values (@tenant, @version, 'PENDING', null);
""", new { tenant = database.TenantId, version = Guid.NewGuid() }));
        Assert.Equal("23502", nullInsert.SqlState);

        var jobs = new GedProcessingJobRepository(database.Factory, NullLogger<GedProcessingJobRepository>.Instance);
        var documentId = Guid.NewGuid();
        var durableVersion = Guid.NewGuid();
        await database.Connection.ExecuteAsync("""
insert into ged.processing_job (tenant_id, document_id, document_version_id, job_type, status, payload)
values (@tenant, @documentId, @versionId, 'PREVIEW', 'PENDING', @payload::jsonb);
""", new { tenant = database.TenantId, documentId, versionId = durableVersion, payload = "{\"storagePath\":\"orig\",\"fileName\":\"a.docx\"}" });
        var firstClaim = await jobs.DequeueAsync("worker-a", 1, "PREVIEW", CancellationToken.None);
        Assert.Single(firstClaim);
        Assert.Equal(1, firstClaim[0].AttemptCount);
        Assert.Contains("orig", firstClaim[0].PayloadJson);
        await database.Connection.ExecuteAsync("update ged.processing_job set locked_at=now() - interval '2 hours' where id=@id", new { id = firstClaim[0].Id });

        var restarted = await database.ReopenAsync();
        var restartedJobs = new GedProcessingJobRepository(restarted, NullLogger<GedProcessingJobRepository>.Instance);
        var recovered = await restartedJobs.RecoverAbandonedAsync("PREVIEW", TimeSpan.FromMinutes(20), CancellationToken.None);
        Assert.Equal(1, recovered);
        var secondClaim = await restartedJobs.DequeueAsync("worker-b", 1, "PREVIEW", CancellationToken.None);
        Assert.Single(secondClaim);
        Assert.Equal(firstClaim[0].Id, secondClaim[0].Id);
        Assert.Equal(2, secondClaim[0].AttemptCount);
        var stolen = await jobs.DequeueAsync("worker-c", 5, null, CancellationToken.None);
        Assert.DoesNotContain(stolen, job => job.JobType == "PREVIEW");
        Assert.Null(await repo.GetAsync(Guid.NewGuid(), versionId, CancellationToken.None));
    }

    [LocalPgFact]
    public async Task Preview_schema_accepts_clean_install_and_upgrade_and_keeps_duplicates()
    {
        await using var installed = await ScratchDatabase.CreateAsync();
        await installed.ExecuteAsync("create schema if not exists ged;");
        await installed.ExecuteFileAsync("database/migrations/2026_10_14_preview_status_upsert_contract.sql");
        var nullable = await installed.Connection.ExecuteScalarAsync<string>("""
select is_nullable from information_schema.columns
where table_schema='ged' and table_name='preview_status' and column_name='requested_at';
""");
        Assert.Equal("YES", nullable);
        var tenant = installed.TenantId;
        var upgradedVersion = Guid.NewGuid();
        await installed.Connection.ExecuteAsync("""
insert into ged.preview_status (tenant_id, document_version_id, status, requested_at, finished_at, attempts, last_attempt_at)
values (@tenant, @version, 'FAILED', null, '2024-06-01T00:00:00Z', 1, null);
""", new { tenant, version = upgradedVersion });
        await installed.ExecuteFileAsync("database/migrations/2026_10_23_preview_status_requested_at_contract.sql");
        await installed.ExecuteFileAsync("database/migrations/2026_10_24_preview_attempt_and_recovery.sql");

        var columns = (await installed.Connection.QueryAsync<(string Name, string Nullable, string? Default, string Type)>("""
select column_name, is_nullable, column_default, udt_name
from information_schema.columns
where table_schema='ged' and table_name='preview_status';
""")).ToDictionary(row => row.Name, row => row, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("NO", columns["requested_at"].Nullable);
        Assert.False(string.IsNullOrWhiteSpace(columns["requested_at"].Default));
        Assert.Equal("YES", columns["finished_at"].Nullable);
        Assert.Equal("NO", columns["attempts"].Nullable);
        Assert.Contains("0", columns["attempts"].Default);
        Assert.Equal("YES", columns["last_attempt_at"].Nullable);
        Assert.Equal("preview_processing_status", columns["status"].Type);

        var labels = (await installed.Connection.QueryAsync<string>("""
select e.enumlabel
from pg_enum e
join pg_type t on t.oid = e.enumtypid
join pg_namespace n on n.oid = t.typnamespace
where n.nspname='ged' and t.typname='preview_processing_status'
order by e.enumsortorder;
""")).ToArray();
        Assert.Equal(new[] { "PENDING", "PROCESSING", "READY", "FAILED", "CANCELED", "ERROR" }, labels);

        var indexDef = await installed.Connection.ExecuteScalarAsync<string>("""
select indexdef from pg_indexes
where schemaname='ged' and indexname='ux_preview_status_tenant_version';
""");
        Assert.Contains("UNIQUE", indexDef, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("(tenant_id, document_version_id)", indexDef, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WHERE", indexDef, StringComparison.OrdinalIgnoreCase);

        var backfilled = await installed.Connection.ExecuteScalarAsync<DateTime>("select requested_at from ged.preview_status where document_version_id=@version", new { version = upgradedVersion });
        var backfilledUtc = backfilled.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(backfilled, DateTimeKind.Utc) : backfilled.ToUniversalTime();
        Assert.Equal(new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc), backfilledUtc);
        var conflict = await Assert.ThrowsAsync<PostgresException>(() => installed.Connection.ExecuteAsync("""
insert into ged.preview_status (tenant_id, document_version_id, status)
values (@tenant, @version, 'PENDING');
""", new { tenant, version = upgradedVersion }));
        Assert.Equal("23505", conflict.SqlState);
        Assert.Equal(1, await installed.Connection.ExecuteScalarAsync<int>("select count(*) from ged.preview_status where document_version_id=@version", new { version = upgradedVersion }));

        await using var duplicated = await ScratchDatabase.CreateAsync();
        await duplicated.ExecuteAsync("""
create schema if not exists ged;
create table ged.preview_status (tenant_id uuid null, document_version_id uuid null, status text null);
insert into ged.preview_status (tenant_id, document_version_id, status) values
 ('11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222', 'PENDING'),
 ('11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222', 'FAILED');
""");
        var blocked = await Assert.ThrowsAsync<PostgresException>(() => duplicated.ExecuteFileAsync("database/migrations/2026_10_14_preview_status_upsert_contract.sql"));
        Assert.Equal("P0001", blocked.SqlState);
        Assert.Equal(2, await duplicated.Connection.ExecuteScalarAsync<int>("select count(*) from ged.preview_status"));
        Assert.Equal(0, await duplicated.Connection.ExecuteScalarAsync<int>("select count(*) from pg_indexes where schemaname='ged' and indexname='ux_preview_status_tenant_version'"));
        var migrationRoot = new DirectoryInfo(AppContext.BaseDirectory);
        while (migrationRoot is not null && !File.Exists(Path.Combine(migrationRoot.FullName, "InovaGed.sln")))
            migrationRoot = migrationRoot.Parent;
        var migration = await File.ReadAllTextAsync(Path.Combine(migrationRoot!.FullName, "database/migrations/2026_10_14_preview_status_upsert_contract.sql"));
        Assert.Contains("RAISE EXCEPTION", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("delete from ged.preview_status", migration, StringComparison.OrdinalIgnoreCase);
    }

    [LocalPgFact]
    public async Task Classification_display_keeps_versioned_legacy_empty_and_broken_links_distinct()
    {
        await using var database = await ScratchDatabase.CreateAsync();
        await database.ExecuteAsync("""
create schema if not exists ged;
create table ged.classification_plan (id uuid primary key, tenant_id uuid not null, code text, name text);
create table ged.classification_plan_version_item (tenant_id uuid not null, version_id uuid not null, classification_id uuid not null, code text, name text);
create table ged.document (id uuid primary key, tenant_id uuid not null, classification_id uuid, classification_version_id uuid);
""");
        var tenant = database.TenantId;
        var plan = Guid.NewGuid();
        var version = Guid.NewGuid();
        var named = Guid.NewGuid();
        var codeOnly = Guid.NewGuid();
        var blank = Guid.NewGuid();
        var legacy = Guid.NewGuid();
        var brokenPlan = Guid.NewGuid();
        await database.Connection.ExecuteAsync("""
insert into ged.classification_plan (id, tenant_id, code, name) values
 (@plan, @tenant, '01', 'Plano que não pode mascarar o vínculo'),
 (@legacy, @tenant, '03', 'Classe Legada'),
 (@blank, @tenant, '', '');
insert into ged.classification_plan_version_item (tenant_id, version_id, classification_id, code, name) values
 (@tenant, @version, @plan, '01.01', 'Contrato Social'),
 (@tenant, @version, @codeOnly, '04.01', ''),
 (@tenant, @version, @blank, '', '');
""", new { tenant, plan, version, codeOnly, blank, legacy });
        await database.Connection.ExecuteAsync("""
insert into ged.document (id, tenant_id, classification_id, classification_version_id) values
 (@namedDoc, @tenant, @plan, @version),
 (@codeDoc, @tenant, @codeOnly, @version),
 (@blankDoc, @tenant, @blank, @version),
 (@legacyDoc, @tenant, @legacy, null),
 (@brokenDoc, @tenant, @brokenPlan, @version),
 (@noneDoc, @tenant, null, null);
""", new { tenant, plan, version, codeOnly, blank, legacy, brokenPlan, namedDoc = Guid.NewGuid(), codeDoc = Guid.NewGuid(), blankDoc = Guid.NewGuid(), legacyDoc = Guid.NewGuid(), brokenDoc = Guid.NewGuid(), noneDoc = Guid.NewGuid() });

        var rows = (await database.Connection.QueryAsync<(Guid Id, string Label, string? Code, string? Name)>($"""
select d.id, {ArchivalClassificationSql.Label()} as label, {ArchivalClassificationSql.Code()} as code, {ArchivalClassificationSql.Name()} as name
from ged.document d
left join ged.classification_plan cp on cp.tenant_id=d.tenant_id and cp.id=d.classification_id
left join ged.classification_plan_version_item pvi on pvi.tenant_id=d.tenant_id and pvi.version_id=d.classification_version_id and pvi.classification_id=d.classification_id;
""")).ToDictionary(row => row.Id);

        var documents = (await database.Connection.QueryAsync<(Guid Id, Guid? ClassificationId, Guid? VersionId)>("select id, classification_id, classification_version_id from ged.document")).ToArray();
        string LabelOf(Func<(Guid Id, Guid? ClassificationId, Guid? VersionId), bool> predicate) => rows[documents.Single(predicate).Id].Label;
        Assert.Equal("01.01 - Contrato Social", LabelOf(row => row.VersionId == version && row.ClassificationId == plan));
        Assert.Equal("04.01", LabelOf(row => row.ClassificationId == codeOnly));
        Assert.Equal(ArchivalClassificationSql.Unidentified, LabelOf(row => row.ClassificationId == blank));
        Assert.Equal("03 - Classe Legada", LabelOf(row => row.ClassificationId == legacy));
        Assert.Equal(ArchivalClassificationSql.BrokenLink, LabelOf(row => row.ClassificationId == brokenPlan));
        Assert.Equal(ArchivalClassificationSql.Unclassified, LabelOf(row => row.ClassificationId is null));
        Assert.DoesNotContain(rows.Values, row => row.Label == "01 - Plano que não pode mascarar o vínculo" && row.Code == "01.01");
        Assert.Equal(ArchivalClassificationSql.BrokenLink, rows[documents.Single(row => row.ClassificationId == brokenPlan).Id].Name);
        Assert.Null(rows[documents.Single(row => row.ClassificationId == brokenPlan).Id].Code);
    }

    [LocalPgFact]
    public async Task Retention_queries_use_class_name_without_pvi_title()
    {
        await using var database = await ScratchDatabase.CreateAsync();
        await database.ExecuteAsync("""
create schema if not exists ged;
create table ged.classification_plan (
    id uuid primary key, tenant_id uuid not null, code text, name text,
    retention_start_event text, final_destination text,
    retention_active_days int, retention_active_months int, retention_active_years int,
    retention_archive_days int, retention_archive_months int, retention_archive_years int);
create table ged.classification_plan_version (id uuid primary key, tenant_id uuid not null, version_no int, title text);
create table ged.classification_plan_version_item (
    tenant_id uuid not null, version_id uuid not null, classification_id uuid not null,
    code text, name text, retention_start_event text, final_destination text,
    retention_active_days int, retention_active_months int, retention_active_years int,
    retention_archive_days int, retention_archive_months int, retention_archive_years int);
create table ged.document (
    id uuid primary key, tenant_id uuid not null, created_at timestamptz, archived_at timestamptz, closed_at timestamptz,
    classification_id uuid, classification_version_id uuid, retention_hold boolean,
    retention_due_at timestamptz, retention_status text, retention_basis_at timestamptz);
""");
        var missingTitle = await Assert.ThrowsAsync<PostgresException>(() => database.Connection.ExecuteScalarAsync("select pvi." + "title from ged.classification_plan_version_item pvi"));
        Assert.Equal("42703", missingTitle.SqlState);

        var tenant = database.TenantId;
        var versionedClass = Guid.NewGuid();
        var historicalClass = Guid.NewGuid();
        var legacyClass = Guid.NewGuid();
        var emptyNameClass = Guid.NewGuid();
        var incompleteClass = Guid.NewGuid();
        var currentVersion = Guid.NewGuid();
        var historicalVersion = Guid.NewGuid();
        var newerVersion = Guid.NewGuid();
        var versionedDocument = Guid.NewGuid();
        var historicalDocument = Guid.NewGuid();
        var legacyDocument = Guid.NewGuid();
        var emptyNameDocument = Guid.NewGuid();
        var incompleteDocument = Guid.NewGuid();
        var unclassifiedDocument = Guid.NewGuid();
        var heldDocument = Guid.NewGuid();
        await database.Connection.ExecuteAsync("""
insert into ged.classification_plan (id, tenant_id, code, name, retention_start_event, final_destination, retention_active_years)
values
 (@versionedClass, @tenant, '01', 'Plano legado ignorado', 'ABERTURA', 'ELIMINAR', 9),
 (@historicalClass, @tenant, '02', 'Plano legado ignorado', 'ABERTURA', 'ELIMINAR', 9),
 (@legacyClass, @tenant, '03', 'Classe Legada', 'ABERTURA', 'ELIMINAR', 2),
 (@emptyNameClass, @tenant, '04', 'Nome do plano', 'ABERTURA', 'ELIMINAR', 1),
 (@incompleteClass, @tenant, '05', 'Plano sem regra', null, null, null);
insert into ged.classification_plan_version (id, tenant_id, version_no, title) values
 (@currentVersion, @tenant, 3, 'Plano Institucional 2024'),
 (@historicalVersion, @tenant, 1, 'Titulo da versao historica'),
 (@newerVersion, @tenant, 2, 'Titulo da versao nova');
insert into ged.classification_plan_version_item (tenant_id, version_id, classification_id, code, name, retention_start_event, final_destination, retention_active_years) values
 (@tenant, @currentVersion, @versionedClass, '01.01', 'Contrato Social', 'ABERTURA', 'ELIMINAR', 1),
 (@tenant, @historicalVersion, @historicalClass, '02.01', 'Nome Antigo', 'ABERTURA', 'ELIMINAR', 1),
 (@tenant, @newerVersion, @historicalClass, '02.01', 'Nome Novo', 'ABERTURA', 'ELIMINAR', 5),
 (@tenant, @currentVersion, @emptyNameClass, '04.01', '', 'ABERTURA', 'ELIMINAR', 1),
 (@tenant, @currentVersion, @incompleteClass, '05.01', 'Sem Prazo', null, null, null);
insert into ged.document (id, tenant_id, created_at, classification_id, classification_version_id, retention_hold) values
 (@versionedDocument, @tenant, '2020-01-15T00:00:00Z', @versionedClass, @currentVersion, false),
 (@historicalDocument, @tenant, '2020-01-15T00:00:00Z', @historicalClass, @historicalVersion, false),
 (@legacyDocument, @tenant, '2020-01-15T00:00:00Z', @legacyClass, null, false),
 (@emptyNameDocument, @tenant, '2020-01-15T00:00:00Z', @emptyNameClass, @currentVersion, false),
 (@incompleteDocument, @tenant, '2020-01-15T00:00:00Z', @incompleteClass, @currentVersion, false),
 (@unclassifiedDocument, @tenant, '2020-01-15T00:00:00Z', null, null, false),
 (@heldDocument, @tenant, '2020-01-15T00:00:00Z', @versionedClass, @currentVersion, true);
""", new { tenant, versionedClass, historicalClass, legacyClass, emptyNameClass, incompleteClass, currentVersion, historicalVersion, newerVersion, versionedDocument, historicalDocument, legacyDocument, emptyNameDocument, incompleteDocument, unclassifiedDocument, heldDocument });

        var repository = new RetentionJobRepository(database.Factory, NullLogger<RetentionJobRepository>.Instance);
        var versioned = await repository.SimulateCalculationAsync(tenant, versionedDocument, 30, CancellationToken.None);
        var historical = await repository.SimulateCalculationAsync(tenant, historicalDocument, 30, CancellationToken.None);
        var legacy = await repository.SimulateCalculationAsync(tenant, legacyDocument, 30, CancellationToken.None);
        var emptyName = await repository.SimulateCalculationAsync(tenant, emptyNameDocument, 30, CancellationToken.None);
        var incomplete = await repository.SimulateCalculationAsync(tenant, incompleteDocument, 30, CancellationToken.None);
        var unclassified = await repository.SimulateCalculationAsync(tenant, unclassifiedDocument, 30, CancellationToken.None);

        Assert.Equal("Contrato Social", versioned!.ClassificationName);
        Assert.Equal("Plano Institucional 2024", versioned.PlanVersionTitle);
        Assert.NotEqual(versioned.PlanVersionTitle, versioned.ClassificationName);
        Assert.Equal("OVERDUE", versioned.CalculatedStatus);
        Assert.Equal(new DateTime(2021, 1, 15), versioned.CalculatedDueAt!.Value.Date);

        Assert.Equal("Nome Antigo", historical!.ClassificationName);
        Assert.Equal("Titulo da versao historica", historical.PlanVersionTitle);
        Assert.Equal("Classe Legada", legacy!.ClassificationName);
        Assert.Equal("LEGACY_PLAN", legacy.RuleSource);
        Assert.Equal("04.01", emptyName!.ClassificationName);
        Assert.Equal("Sem Prazo", incomplete!.ClassificationName);
        Assert.True(incomplete.IsRuleIncomplete);
        Assert.Null(incomplete.CalculatedDueAt);
        Assert.Equal("INCOMPLETE_RULE", incomplete.CalculatedStatus);
        Assert.Null(unclassified!.ClassificationName);
        Assert.Equal("SEM_CLASSIFICACAO", unclassified.CalculatedStatus);

        Assert.Equal(1, await repository.RecalculateOneAsync(tenant, versionedDocument, 30, CancellationToken.None));
        Assert.True(await repository.RecalculateAsync(tenant, 30, CancellationToken.None) >= 1);
        var persisted = await database.Connection.QuerySingleAsync<(string? Status, DateTime? Due)>("select retention_status as Status, retention_due_at as Due from ged.document where id=@id", new { id = versionedDocument });
        Assert.Equal("OVERDUE", persisted.Status);
        Assert.Equal(new DateTime(2021, 1, 15), persisted.Due!.Value.Date);

        Assert.Equal(1, await repository.RecalculateOneAsync(tenant, heldDocument, 30, CancellationToken.None));
        var held = await database.Connection.QuerySingleAsync<(bool Hold, string? Status)>("select retention_hold as Hold, retention_status as Status from ged.document where id=@id", new { id = heldDocument });
        Assert.True(held.Hold);
        Assert.Equal("HOLD", held.Status);
    }

    [LocalPgFact]
    public async Task Published_plan_version_is_not_the_highest_number_when_unpublished()
    {
        await using var database = await ScratchDatabase.CreateAsync();
        await database.ExecuteAsync("""
create schema if not exists ged;
create table ged.classification_plan_version (
    id uuid primary key,
    tenant_id uuid not null,
    version_no int not null,
    published_at timestamptz null,
    reg_status char(1) not null default 'A');
create table ged.document (
    id uuid primary key,
    tenant_id uuid not null,
    title text,
    xmin_check bigint);
""");
        var published = Guid.NewGuid();
        var draft = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        await database.Connection.ExecuteAsync("""
insert into ged.classification_plan_version (id, tenant_id, version_no, published_at) values
 (@published, @tenant, 2, '2024-01-01T00:00:00Z'),
 (@draft, @tenant, 9, null);
insert into ged.document (id, tenant_id, title) values (@documentId, @tenant, 'original');
""", new { published, draft, documentId, tenant = database.TenantId });

        var current = await database.Connection.ExecuteScalarAsync<Guid>($"select {PlanVersionSql.CurrentId("@tenant")}", new { tenant = database.TenantId });
        Assert.Equal(published, current);

        var token = await database.Connection.ExecuteScalarAsync<long>($"select {DocumentConcurrency.TokenSelect()} from ged.document where id=@id", new { id = documentId });
        var changed = await database.Connection.ExecuteAsync($"update ged.document set title='confirmado' where id=@id and {DocumentConcurrency.TokenSelect()}=@token", new { id = documentId, token });
        Assert.Equal(1, changed);
        var lost = await database.Connection.ExecuteAsync($"update ged.document set title='sobrescrito' where id=@id and {DocumentConcurrency.TokenSelect()}=@token", new { id = documentId, token });
        Assert.Equal(0, lost);
        Assert.Equal("confirmado", await database.Connection.ExecuteScalarAsync<string>("select title from ged.document where id=@id", new { id = documentId }));
    }

    [LocalPgFact]
    public async Task Preview_varchar_status_becomes_enum_and_upsert_covers_each_state()
    {
        await using var database = await ScratchDatabase.CreateAsync();
        var tenant = database.TenantId;
        var preservedError = Guid.NewGuid();
        var preservedCancel = Guid.NewGuid();
        await database.Connection.ExecuteAsync("""
create schema if not exists ged;
create table ged.preview_status (
    tenant_id uuid not null,
    document_version_id uuid not null,
    status varchar(32) not null,
    preview_path text null,
    error_message text null,
    requested_at timestamptz null,
    finished_at timestamptz null,
    attempts integer not null default 0,
    last_attempt_at timestamptz null
);
insert into ged.preview_status (tenant_id, document_version_id, status, error_message, requested_at, attempts)
values
 (@tenant, @preservedError, 'ERROR', 'falha antiga', '2024-01-02T00:00:00Z', 4),
 (@tenant, @preservedCancel, 'CANCELLED', null, '2024-01-03T00:00:00Z', 1);
""", new { tenant, preservedError, preservedCancel });

        await database.ExecuteFileAsync("database/migrations/2026_10_14_preview_status_upsert_contract.sql");
        await database.ExecuteFileAsync("database/migrations/2026_10_23_preview_status_requested_at_contract.sql");
        await database.ExecuteFileAsync("database/migrations/2026_10_24_preview_attempt_and_recovery.sql");
        await database.ExecuteFileAsync("database/migrations/2026_11_07_preview_status_enum_operator_contract.sql");
        await database.ExecuteFileAsync("database/migrations/2026_11_07_preview_status_enum_operator_contract.sql");

        Assert.Equal("preview_processing_status", await database.Connection.ExecuteScalarAsync<string>("""
select udt_name from information_schema.columns
where table_schema='ged' and table_name='preview_status' and column_name='status';
"""));
        Assert.Equal("ERROR", await database.Connection.ExecuteScalarAsync<string>("select status::text from ged.preview_status where document_version_id=@id", new { id = preservedError }));
        Assert.Equal(4, await database.Connection.ExecuteScalarAsync<int>("select attempts from ged.preview_status where document_version_id=@id", new { id = preservedError }));
        Assert.Equal("falha antiga", await database.Connection.ExecuteScalarAsync<string>("select error_message from ged.preview_status where document_version_id=@id", new { id = preservedError }));
        Assert.Equal("CANCELED", await database.Connection.ExecuteScalarAsync<string>("select status::text from ged.preview_status where document_version_id=@id", new { id = preservedCancel }));

        var repo = new PreviewStatusRepository(database.Factory, NullLogger<PreviewStatusRepository>.Instance);
        var requestedAt = DateTimeOffset.Parse("2026-10-16T12:00:00Z");
        var readyVersion = Guid.NewGuid();
        var readyAttempt = Guid.NewGuid();
        await repo.UpsertAsync(tenant, readyVersion, PreviewProcessingStatus.Pending, null, null, requestedAt, null, CancellationToken.None);
        await repo.UpsertAsync(Write(tenant, readyVersion, PreviewProcessingStatus.Processing, requestedAt, readyAttempt), CancellationToken.None);
        await repo.UpsertAsync(Write(tenant, readyVersion, PreviewProcessingStatus.Ready, requestedAt, readyAttempt, path: "previews/pronto.pdf", confirm: true), CancellationToken.None);
        var ready = await repo.GetAsync(tenant, readyVersion, CancellationToken.None);
        Assert.Equal(PreviewProcessingStatus.Ready, ready!.Status);
        Assert.Equal("previews/pronto.pdf", ready.PreviewPath);
        Assert.Equal(1, ready.Attempts);

        var failedVersion = Guid.NewGuid();
        var failedAttempt = Guid.NewGuid();
        await repo.UpsertAsync(tenant, failedVersion, PreviewProcessingStatus.Pending, null, null, requestedAt, null, CancellationToken.None);
        await repo.UpsertAsync(Write(tenant, failedVersion, PreviewProcessingStatus.Processing, requestedAt, failedAttempt), CancellationToken.None);
        await repo.UpsertAsync(Write(tenant, failedVersion, PreviewProcessingStatus.Error, requestedAt, failedAttempt, error: "conversão"), CancellationToken.None);
        var recoveredAttempt = Guid.NewGuid();
        await repo.UpsertAsync(Write(tenant, failedVersion, PreviewProcessingStatus.Processing, requestedAt, recoveredAttempt), CancellationToken.None);
        await repo.UpsertAsync(Write(tenant, failedVersion, PreviewProcessingStatus.Ready, requestedAt, recoveredAttempt, path: "previews/recuperado.pdf", confirm: true), CancellationToken.None);
        var recovered = await repo.GetAsync(tenant, failedVersion, CancellationToken.None);
        Assert.Equal(PreviewProcessingStatus.Ready, recovered!.Status);
        Assert.Equal(2, recovered.Attempts);
        Assert.Null(recovered.ErrorMessage);

        var canceledVersion = Guid.NewGuid();
        await repo.UpsertAsync(tenant, canceledVersion, PreviewProcessingStatus.Pending, null, null, requestedAt, null, CancellationToken.None);
        await repo.UpsertAsync(tenant, canceledVersion, PreviewProcessingStatus.Canceled, null, "cancelado pelo operador", requestedAt, DateTimeOffset.UtcNow, CancellationToken.None);
        var canceled = await repo.GetAsync(tenant, canceledVersion, CancellationToken.None);
        Assert.Equal(PreviewProcessingStatus.Canceled, canceled!.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.UpsertAsync(tenant, canceledVersion, PreviewProcessingStatus.Ready, "previews/nao.pdf", null, requestedAt, DateTimeOffset.UtcNow, CancellationToken.None));
        Assert.Equal(PreviewProcessingStatus.Canceled, (await repo.GetAsync(tenant, canceledVersion, CancellationToken.None))!.Status);

        Assert.Null(await repo.GetAsync(Guid.NewGuid(), readyVersion, CancellationToken.None));
        Assert.Equal(1, await database.Connection.ExecuteScalarAsync<int>("select count(*) from ged.preview_status where tenant_id=@tenant and document_version_id=@version", new { tenant, version = readyVersion }));

        var concurrentVersion = Guid.NewGuid();
        await Task.WhenAll(
            repo.UpsertAsync(tenant, concurrentVersion, PreviewProcessingStatus.Pending, null, null, requestedAt, null, CancellationToken.None),
            repo.UpsertAsync(tenant, concurrentVersion, PreviewProcessingStatus.Pending, null, null, requestedAt, null, CancellationToken.None));
        Assert.Equal(1, await database.Connection.ExecuteScalarAsync<int>("select count(*) from ged.preview_status where tenant_id=@tenant and document_version_id=@version", new { tenant, version = concurrentVersion }));
        Assert.Equal("PENDING", await database.Connection.ExecuteScalarAsync<string>("select status::text from ged.preview_status where document_version_id=@version", new { version = concurrentVersion }));
    }

    [LocalPgFact]
    public async Task Preview_status_migration_keeps_unknown_label_and_does_not_delete_rows()
    {
        await using var database = await ScratchDatabase.CreateAsync();
        var versionId = Guid.NewGuid();
        await database.Connection.ExecuteAsync("""
create schema if not exists ged;
create table ged.preview_status (
    tenant_id uuid not null,
    document_version_id uuid not null,
    status varchar(32) not null,
    requested_at timestamptz not null
);
insert into ged.preview_status (tenant_id, document_version_id, status, requested_at)
values (@tenant, @version, 'BOGUS', now());
""", new { tenant = database.TenantId, version = versionId });

        var blocked = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteFileAsync("database/migrations/2026_11_07_preview_status_enum_operator_contract.sql"));
        Assert.Equal("P0001", blocked.SqlState);
        Assert.Equal(1, await database.Connection.ExecuteScalarAsync<int>("select count(*) from ged.preview_status"));
        Assert.Equal("BOGUS", await database.Connection.ExecuteScalarAsync<string>("select status from ged.preview_status where document_version_id=@version", new { version = versionId }));
        Assert.Equal("character varying", await database.Connection.ExecuteScalarAsync<string>("""
select data_type from information_schema.columns
where table_schema='ged' and table_name='preview_status' and column_name='status';
"""));
    }

    [LocalPgFact]
    public async Task Preview_enum_migration_preserves_update_of_status_trigger()
    {
        await using var database = await ScratchDatabase.CreateAsync();
        var versionId = Guid.NewGuid();
        await database.ExecuteAsync("create schema if not exists ged;");
        await database.Connection.ExecuteAsync("""
create table ged.preview_status (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    document_version_id uuid not null,
    status varchar not null default 'PENDING',
    requested_at timestamptz not null default now(),
    requested_at_kind text not null default 'ORIGINAL'
);
create unique index ux_preview_status_tenant_version on ged.preview_status (tenant_id, document_version_id);
create function ged.sector_processing_notification() returns trigger language plpgsql as $$ begin return new; end $$;
create trigger sector_processing_preview after insert or update of status on ged.preview_status for each row execute function ged.sector_processing_notification();
insert into ged.preview_status (tenant_id, document_version_id, status) values (@tenant, @version, 'PENDING');
""", new { tenant = database.TenantId, version = versionId });

        await database.ExecuteFileAsync("database/migrations/2026_11_07_preview_status_enum_operator_contract.sql");

        Assert.Equal("preview_processing_status", await database.Connection.ExecuteScalarAsync<string>("""
select udt_name from information_schema.columns
where table_schema='ged' and table_name='preview_status' and column_name='status';
"""));
        Assert.Equal(1, await database.Connection.ExecuteScalarAsync<int>("select count(*) from pg_trigger where tgname='sector_processing_preview' and not tgisinternal"));
        Assert.Equal("PENDING", await database.Connection.ExecuteScalarAsync<string>("select status::text from ged.preview_status where document_version_id=@version", new { version = versionId }));
    }

    [LocalPgFact]
    public async Task Processing_center_keeps_tenant_scope_and_does_not_duplicate_an_active_job()
    {
        await using var database = await ScratchDatabase.CreateAsync();
        var otherTenant = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var failedId = Guid.NewGuid();
        var activeId = Guid.NewGuid();
        var foreignId = Guid.NewGuid();
        await database.ExecuteAsync("""
create schema if not exists ged;
create table ged.document (
    id uuid primary key,
    tenant_id uuid not null,
    title text not null,
    created_by uuid null,
    document_type_id uuid null
);
create table ged.processing_job (
    id uuid primary key,
    tenant_id uuid not null,
    document_id uuid null,
    document_version_id uuid null,
    upload_batch_id uuid null,
    upload_batch_item_id uuid null,
    job_type text not null,
    status text not null,
    priority int not null default 5,
    attempt_count int not null default 0,
    max_attempts int not null default 3,
    error_message text null,
    started_at timestamptz null,
    finished_at timestamptz null,
    next_attempt_at timestamptz null,
    locked_by text null,
    locked_at timestamptz null,
    created_at timestamptz not null default now(),
    updated_at timestamptz null,
    reg_status char(1) not null default 'A'
);
""");
        await database.Connection.ExecuteAsync("""
insert into ged.document (id, tenant_id, title) values (@documentId, @tenantId, 'Contrato de manutenção');
insert into ged.processing_job (id, tenant_id, document_id, document_version_id, job_type, status, attempt_count, max_attempts, error_message)
values (@failedId, @tenantId, @documentId, @versionId, 'PREVIEW', 'FAILED', 3, 3, 'conversão');
insert into ged.processing_job (id, tenant_id, document_id, document_version_id, job_type, status)
values (@activeId, @tenantId, @documentId, @versionId, 'PREVIEW', 'PENDING');
insert into ged.processing_job (id, tenant_id, document_id, document_version_id, job_type, status, error_message)
values (@foreignId, @otherTenant, null, @versionId, 'PREVIEW', 'FAILED', 'outro tenant');
""", new { documentId, tenantId = database.TenantId, versionId, failedId, activeId, foreignId, otherTenant });

        var args = new
        {
            tenantId = database.TenantId,
            from = DateTimeOffset.UtcNow.AddDays(-1),
            to = DateTimeOffset.UtcNow.AddDays(1),
            lane = (string?)null,
            statuses = new[] { "FAILED", "PENDING", "ERROR" },
            name = "%Contrato%",
            userId = (Guid?)null,
            documentTypeId = (Guid?)null,
            take = 20
        };
        var rows = (await database.Connection.QueryAsync(ProcessingCenterSql.Jobs, args)).AsList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(0, await database.Connection.ExecuteAsync(ProcessingCenterSql.RetryJob, new { tenantId = database.TenantId, jobId = failedId }));
        Assert.Equal("FAILED", await database.Connection.ExecuteScalarAsync<string>("select status from ged.processing_job where id=@failedId", new { failedId }));

        Assert.Equal(1, await database.Connection.ExecuteAsync(ProcessingCenterSql.CancelJob, new { tenantId = database.TenantId, jobId = activeId }));
        Assert.Equal(1, await database.Connection.ExecuteAsync(ProcessingCenterSql.RetryJob, new { tenantId = database.TenantId, jobId = failedId }));
        var retried = await database.Connection.QuerySingleAsync("select status, attempt_count as attempts from ged.processing_job where id=@failedId", new { failedId });
        Assert.Equal("PENDING", (string)retried.status);
        Assert.Equal(0, (int)retried.attempts);
        Assert.Equal("FAILED", await database.Connection.ExecuteScalarAsync<string>("select status from ged.processing_job where id=@foreignId", new { foreignId }));
    }

    [LocalPgFact]
    public async Task Jobs_abandonados_de_outros_tipos_voltam_a_fila_e_o_preview_permanece_com_o_worker_dele()
    {
        await using var database = await ScratchDatabase.CreateAsync();
        await database.ExecuteAsync("""
create schema if not exists ged;
create table ged.processing_job (
    id uuid primary key,
    tenant_id uuid not null,
    document_id uuid null,
    document_version_id uuid null,
    upload_batch_id uuid null,
    upload_batch_item_id uuid null,
    job_type text not null,
    status text not null,
    priority int not null default 5,
    attempt_count int not null default 0,
    max_attempts int not null default 3,
    error_message text null,
    started_at timestamptz null,
    finished_at timestamptz null,
    next_attempt_at timestamptz null,
    locked_by text null,
    locked_at timestamptz null,
    created_at timestamptz not null default now(),
    updated_at timestamptz null,
    reg_status char(1) not null default 'A'
);
""");
        var tenant = database.TenantId;
        var indexId = Guid.NewGuid();
        var classId = Guid.NewGuid();
        var ocrId = Guid.NewGuid();
        var previewId = Guid.NewGuid();
        var liveId = Guid.NewGuid();
        await database.Connection.ExecuteAsync("""
insert into ged.processing_job (id, tenant_id, job_type, status, locked_by, locked_at)
values
 (@indexId, @tenant, 'SMART_INDEX', 'PROCESSING', 'morto', now() - interval '30 minutes'),
 (@classId, @tenant, 'CLASSIFICATION', 'PROCESSING', 'morto', now() - interval '30 minutes'),
 (@ocrId, @tenant, 'OCR', 'PROCESSING', null, null),
 (@previewId, @tenant, 'PREVIEW', 'PROCESSING', 'preview', now() - interval '30 minutes'),
 (@liveId, @tenant, 'SECTOR_EXTRACT', 'PROCESSING', 'vivo', now());
""", new { tenant, indexId, classId, ocrId, previewId, liveId });

        var jobs = new GedProcessingJobRepository(database.Factory, NullLogger<GedProcessingJobRepository>.Instance);
        var recovered = await jobs.RecoverAbandonedExceptAsync("PREVIEW", TimeSpan.FromMinutes(10), CancellationToken.None);
        Assert.Equal(3, recovered);
        Assert.Equal("PENDING", await database.Connection.ExecuteScalarAsync<string>("select status from ged.processing_job where id=@indexId", new { indexId }));
        Assert.Equal("PENDING", await database.Connection.ExecuteScalarAsync<string>("select status from ged.processing_job where id=@classId", new { classId }));
        Assert.Equal("PENDING", await database.Connection.ExecuteScalarAsync<string>("select status from ged.processing_job where id=@ocrId", new { ocrId }));
        Assert.Equal("PROCESSING", await database.Connection.ExecuteScalarAsync<string>("select status from ged.processing_job where id=@previewId", new { previewId }));
        Assert.Equal("PROCESSING", await database.Connection.ExecuteScalarAsync<string>("select status from ged.processing_job where id=@liveId", new { liveId }));

        await jobs.TouchLockAsync(tenant, liveId, "vivo", CancellationToken.None);
        Assert.Equal(0, await jobs.RecoverAbandonedExceptAsync("PREVIEW", TimeSpan.FromMinutes(10), CancellationToken.None));
        var claimed = await jobs.DequeueAsync("worker-b", 10, CancellationToken.None);
        Assert.Contains(claimed, job => job.Id == indexId);
        Assert.DoesNotContain(claimed, job => job.Id == previewId || job.Id == liveId);
        Assert.Equal(1, await database.Connection.ExecuteScalarAsync<int>("select count(*) from ged.processing_job where id=@indexId", new { indexId }));
    }

    private static PreviewStatusUpsert Write(Guid tenantId, Guid versionId, PreviewProcessingStatus status, DateTimeOffset requestedAt, Guid attemptId, string? error = null, string? path = null, bool confirm = false)
        => new()
        {
            TenantId = tenantId,
            VersionId = versionId,
            Status = status,
            RequestedAt = requestedAt,
            AttemptId = attemptId,
            ErrorMessage = error,
            PreviewPath = path,
            ConfirmOutcome = confirm,
            FinishedAt = status is PreviewProcessingStatus.Ready or PreviewProcessingStatus.Error ? DateTimeOffset.Parse("2026-10-12T15:10:00Z") : null
        };

    private sealed class ScratchDatabase : IAsyncDisposable
    {
        private readonly string _adminDsn;
        private readonly string _name;
        public NpgsqlConnection Connection { get; }
        public NpgsqlConnectionFactory Factory { get; }
        public Guid TenantId { get; } = Guid.NewGuid();

        private ScratchDatabase(string adminDsn, string name, NpgsqlConnection connection, NpgsqlConnectionFactory factory)
        {
            _adminDsn = adminDsn;
            _name = name;
            Connection = connection;
            Factory = factory;
        }

        public static async Task<ScratchDatabase> CreateAsync()
        {
            var name = "ig_preview_" + Guid.NewGuid().ToString("N");
            var adminBuilder = new NpgsqlConnectionStringBuilder(LocalPg.AdminDsn) { Pooling = false };
            await using var admin = new NpgsqlConnection(adminBuilder.ConnectionString);
            await admin.OpenAsync();
            await admin.ExecuteAsync($"create database {name}");
            var builder = new NpgsqlConnectionStringBuilder(adminBuilder.ConnectionString) { Database = name };
            var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            return new ScratchDatabase(adminBuilder.ConnectionString, name, connection, new NpgsqlConnectionFactory(builder.ConnectionString));
        }

        public async Task ExecuteFileAsync(string relativePath)
        {
            var root = FindRoot();
            var sql = await File.ReadAllTextAsync(Path.Combine(root, relativePath));
            await Connection.ExecuteAsync(sql);
        }

        public Task ExecuteAsync(string sql) => Connection.ExecuteAsync(sql);

        public async Task<NpgsqlConnectionFactory> ReopenAsync()
        {
            var builder = new NpgsqlConnectionStringBuilder(_adminDsn) { Database = _name, Pooling = false };
            var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            await connection.ExecuteScalarAsync("select 1");
            await connection.DisposeAsync();
            return new NpgsqlConnectionFactory(builder.ConnectionString);
        }

        public async ValueTask DisposeAsync()
        {
            await Connection.DisposeAsync();
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(_adminDsn);
            await admin.OpenAsync();
            await admin.ExecuteAsync($"drop database if exists {_name} with (force)");
        }

        private static string FindRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null && !File.Exists(Path.Combine(current.FullName, "InovaGed.sln")))
                current = current.Parent;
            return current?.FullName ?? throw new DirectoryNotFoundException();
        }
    }
}
