using Dapper;
using InovaGed.Application.ArtificialIntelligence;
using InovaGed.Application.Retention;
using InovaGed.Infrastructure.ArtificialIntelligence;
using InovaGed.Infrastructure.Common.Database;
using InovaGed.Infrastructure.Retention;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace InovaGed.Application.Tests;

[Collection("Document AI PostgreSQL")]
public sealed class DocumentAiCyclePostgresTests : IAsyncLifetime
{
    private NpgsqlConnection? _admin;

    public async Task InitializeAsync()
    {
        if (PgGate.UnavailableReason is not null) return;
        _admin = new NpgsqlConnection(PgGate.Dsn());
        await _admin.OpenAsync();
        await _admin.ExecuteAsync(Schema);
    }

    public async Task DisposeAsync() { if (_admin is not null) await _admin.DisposeAsync(); }

    [PgGatedFact]
    public async Task Committed_classification_keeps_a_recoverable_pending_when_recalc_never_runs()
    {
        var fx = await Seed();
        var store = Store();
        var written = await store.ApplyArchivalClassAsync(Record(fx, "Applied", true), fx.Token, fx.ClassId, true, CancellationToken.None);
        Assert.Equal("Applied", written.Code);
        Assert.True(written.RetentionPending);
        Assert.NotNull(written.PendingId);
        Assert.Equal(fx.ClassId, await One<Guid>("select classification_id from ged.document where id=@id", new { id = fx.DocumentId }));
        Assert.Equal(fx.PlanVersionId, await One<Guid>("select classification_version_id from ged.document where id=@id", new { id = fx.DocumentId }));
        Assert.True(await One<bool>("select retention_hold from ged.document where id=@id", new { id = fx.DocumentId }));
        Assert.Equal(1, await One<int>("select count(*) from ged.ai_suggestion_application where document_id=@id", new { id = fx.DocumentId }));
        Assert.Equal(1, await One<int>("select count(*) from ged.app_audit_log where entity_id=@id", new { id = fx.DocumentId.ToString() }));
        Assert.Equal(1, await One<int>("select count(*) from ged.ai_retention_recalc_pending where application_id=@id and resolved_at is null", new { id = written.ApplicationId }));

        var replay = await store.ApplyArchivalClassAsync(Record(fx, "Applied", true), 1, fx.ClassId, true, CancellationToken.None);
        Assert.Equal("AlreadyApplied", replay.Code);
        Assert.True(replay.RetentionPending);
        Assert.Equal(1, await One<int>("select count(*) from ged.ai_suggestion_application where document_id=@id", new { id = fx.DocumentId }));
        Assert.Equal(1, await One<int>("select count(*) from ged.app_audit_log where entity_id=@id", new { id = fx.DocumentId.ToString() }));
        Assert.Equal(1, await One<int>("select count(*) from ged.ai_retention_recalc_pending where document_id=@id", new { id = fx.DocumentId }));

        var other = Record(fx, "Applied", true, ReviewIdentity.CatalogJson(AiTask.SuggestArchivalClassification.ToString(), Guid.NewGuid()));
        var conflict = await store.ApplyArchivalClassAsync(other, fx.Token, Guid.NewGuid(), true, CancellationToken.None);
        Assert.Equal("DecisionConflict", conflict.Code);
        Assert.Equal(1, await One<int>("select count(*) from ged.ai_suggestion_application where document_id=@id", new { id = fx.DocumentId }));

        var history = await store.ListReviewsAsync(fx.TenantId, fx.DocumentId, 0, 10, CancellationToken.None);
        Assert.Contains(history, row => row.Kind == "application" && row.PendingId == written.PendingId && row.DocumentId == fx.DocumentId);

        var token = Guid.NewGuid();
        var claim = await store.ClaimRetentionAsync(fx.TenantId, written.PendingId!.Value, token, CancellationToken.None);
        Assert.NotNull(claim);
        await store.FailRetentionAsync(fx.TenantId, claim!.Id, token, "Laudo do paciente Silva", CancellationToken.None);
        Assert.Equal("temporalidade:falha", await One<string>("select last_error from ged.ai_retention_recalc_pending where id=@id", new { id = written.PendingId }));
        Assert.Null(await One<DateTime?>("select resolved_at from ged.ai_retention_recalc_pending where id=@id", new { id = written.PendingId }));
        var resumeToken = Guid.NewGuid();
        var resumed = await store.ClaimRetentionAsync(fx.TenantId, written.PendingId.Value, resumeToken, CancellationToken.None);
        Assert.NotNull(resumed);
        Assert.Equal(2, resumed!.Attempts);
        await new RetentionRecalcService(new RetentionJobRepository(Factory(), NullLogger<RetentionJobRepository>.Instance), NullLogger<RetentionRecalcService>.Instance).RunOneAsync(fx.TenantId, fx.DocumentId, 30, CancellationToken.None);
        Assert.True(await store.ResolveRetentionAsync(fx.TenantId, resumed.Id, resumeToken, CancellationToken.None));
        Assert.NotNull(await One<DateTime?>("select resolved_at from ged.ai_retention_recalc_pending where id=@id", new { id = written.PendingId }));
        Assert.True(await One<bool>("select retention_hold from ged.document where id=@id", new { id = fx.DocumentId }));
        Assert.Equal("LOAN", await One<string>("select notes from ged.loan_request where document_id=@id", new { id = fx.DocumentId }));
        Assert.False((await store.FindReviewAsync(fx.TenantId, ReviewIdentity.OperationKey(fx.TenantId, fx.ExecutionId, fx.DocumentId, fx.VersionId, fx.ReviewerId, AiTask.SuggestArchivalClassification.ToString()), CancellationToken.None))!.RetentionPending);
    }

    [PgGatedFact]
    public async Task Concurrent_claims_process_one_pending_and_a_stale_claim_can_be_recovered()
    {
        var fx = await Seed();
        var store = Store();
        var written = await store.ApplyDocumentTypeAsync(TypeRecord(fx), fx.Token, fx.TypeId, true, CancellationToken.None);
        Assert.NotNull(written.PendingId);
        var tokenA = Guid.NewGuid();
        var tokenB = Guid.NewGuid();
        var claims = await Task.WhenAll(store.ClaimRetentionAsync(fx.TenantId, written.PendingId!.Value, tokenA, CancellationToken.None), store.ClaimRetentionAsync(fx.TenantId, written.PendingId.Value, tokenB, CancellationToken.None));
        Assert.Equal(1, claims.Count(x => x is not null));
        var winner = claims.Single(x => x is not null)!;
        Assert.Equal(1, winner.Attempts);
        await _admin!.ExecuteAsync("update ged.ai_retention_recalc_pending set claimed_at=now()-interval '5 minutes' where id=@id", new { id = written.PendingId });
        var recovered = await store.ClaimRetentionAsync(fx.TenantId, written.PendingId.Value, Guid.NewGuid(), CancellationToken.None);
        Assert.NotNull(recovered);
        Assert.Equal(2, recovered!.Attempts);
        Assert.Equal(fx.VersionId, await One<Guid>("select document_version_id from ged.document_classification where document_id=@id", new { id = fx.DocumentId }));
        Assert.NotEqual(fx.LaterVersionId, await One<Guid>("select document_version_id from ged.document_classification where document_id=@id", new { id = fx.DocumentId }));
    }

    [PgGatedFact]
    public async Task Archival_apply_conflicts_when_the_class_is_not_in_the_current_plan_version()
    {
        var fx = await Seed();
        await _admin!.ExecuteAsync("insert into ged.classification_plan_version(id,tenant_id,version_no,title) values(@id,@tenant,2,'V2')", new { id = fx.NextPlanVersionId, tenant = fx.TenantId });
        var written = await Store().ApplyArchivalClassAsync(Record(fx, "Applied", true), fx.Token, fx.ClassId, true, CancellationToken.None);
        Assert.Equal("Conflict", written.Code);
        Assert.Null(await One<Guid?>("select classification_id from ged.document where id=@id", new { id = fx.DocumentId }));
        Assert.Equal(0, await One<int>("select count(*) from ged.ai_suggestion_application where document_id=@id", new { id = fx.DocumentId }));
        Assert.Equal(0, await One<int>("select count(*) from ged.ai_retention_recalc_pending where document_id=@id", new { id = fx.DocumentId }));
    }

    private PostgresAssistedDocumentStore Store() => new(Factory());

    private AssistedRetentionRecovery Recovery(IRetentionJobRepository? repository = null) => new(
        Factory(),
        repository ?? new RetentionJobRepository(Factory(), NullLogger<RetentionJobRepository>.Instance));

    [PgGatedFact]
    public async Task New_worker_recovers_committed_pending_and_replay_does_not_recalculate()
    {
        var fx = await Seed();
        var written = await Store().ApplyArchivalClassAsync(Record(fx, "Applied", true), fx.Token, fx.ClassId, true, default);
        Assert.Equal(1, await Recovery().RunBatchAsync(fx.TenantId, default));
        var token = await One<long>("select xmin::text::bigint from ged.document where id=@id", new { id = fx.DocumentId });
        Assert.Equal(0, await Recovery().RunBatchAsync(fx.TenantId, default));
        Assert.True((await Recovery().RunAsync(fx.TenantId, written.PendingId!.Value, true, default)).Resolved);
        Assert.Equal(token, await One<long>("select xmin::text::bigint from ged.document where id=@id", new { id = fx.DocumentId }));
        Assert.False(await One<bool>("select partial from ged.ai_suggestion_application where id=@id", new { id = written.ApplicationId }));
        Assert.True(await One<bool>("select retention_hold from ged.document where id=@id", new { id = fx.DocumentId }));
        Assert.Equal(1, await One<int>("select count(*) from ged.app_audit_log where entity_id=@id", new { id = fx.DocumentId.ToString() }));
    }

    [PgGatedFact]
    public async Task Failure_after_review_completion_rolls_back_calculation_and_both_completion_flags()
    {
        var fx = await Seed();
        var written = await Store().ApplyArchivalClassAsync(Record(fx, "Applied", true), fx.Token, fx.ClassId, true, default);
        var trigger = "fail_completion_" + Guid.NewGuid().ToString("N");
        await _admin!.ExecuteAsync($"""
create function ged.{trigger}() returns trigger language plpgsql as $$
begin if NEW.id='{written.PendingId}' and NEW.resolved_at is not null then raise exception 'private document text'; end if; return NEW; end $$;
create trigger {trigger} before update on ged.ai_retention_recalc_pending for each row execute function ged.{trigger}();
""");
        try
        {
            Assert.False((await Recovery().RunAsync(fx.TenantId, written.PendingId!.Value, true, default)).Resolved);
            Assert.True(await One<bool>("select partial from ged.ai_suggestion_application where id=@id", new { id = written.ApplicationId }));
            Assert.Null(await One<DateTime?>("select resolved_at from ged.ai_retention_recalc_pending where id=@id", new { id = written.PendingId }));
            Assert.Null(await One<DateTime?>("select retention_due_at from ged.document where id=@id", new { id = fx.DocumentId }));
            Assert.Equal("temporalidade:P0001", await One<string>("select last_error from ged.ai_retention_recalc_pending where id=@id", new { id = written.PendingId }));
            Assert.Equal(0, await Recovery().RunBatchAsync(fx.TenantId, default)); // backoff
        }
        finally { await _admin!.ExecuteAsync($"drop trigger {trigger} on ged.ai_retention_recalc_pending; drop function ged.{trigger}();"); }
        Assert.True((await Recovery().RunAsync(fx.TenantId, written.PendingId!.Value, true, default)).Resolved);
        Assert.False(await One<bool>("select partial from ged.ai_suggestion_application where id=@id", new { id = written.ApplicationId }));
    }

    [PgGatedFact]
    public async Task Database_lock_prevents_second_consumer_even_after_the_old_90_second_lease()
    {
        var fx = await Seed();
        var written = await Store().ApplyArchivalClassAsync(Record(fx, "Applied", true), fx.Token, fx.ClassId, true, default);
        var repository = new PausingRepository(new RetentionJobRepository(Factory(), NullLogger<RetentionJobRepository>.Instance));
        var first = Recovery(repository).RunAsync(fx.TenantId, written.PendingId!.Value, true, default);
        await repository.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(92));
            Assert.False((await Recovery().RunAsync(fx.TenantId, written.PendingId.Value, true, default)).Resolved);
        }
        finally { repository.Continue.TrySetResult(); }
        Assert.True((await first).Resolved);
        Assert.Equal(1, await One<int>("select attempts from ged.ai_retention_recalc_pending where id=@id", new { id = written.PendingId }));
    }

    [PgGatedFact]
    public async Task Cancellation_keeps_pending_and_persistent_failures_require_manual_recovery()
    {
        var fx = await Seed();
        var written = await Store().ApplyArchivalClassAsync(Record(fx, "Applied", true), fx.Token, fx.ClassId, true, default);
        var repository = new PausingRepository(new RetentionJobRepository(Factory(), NullLogger<RetentionJobRepository>.Instance));
        using var cts = new CancellationTokenSource();
        var run = Recovery(repository).RunAsync(fx.TenantId, written.PendingId!.Value, true, cts.Token);
        await repository.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.True(await One<bool>("select partial from ged.ai_suggestion_application where id=@id", new { id = written.ApplicationId }));
        Assert.Equal("temporalidade:cancelada", await One<string>("select last_error from ged.ai_retention_recalc_pending where id=@id", new { id = written.PendingId }));
        await _admin!.ExecuteAsync("update ged.ai_retention_recalc_pending set attempts=10, next_attempt_at=now()-interval '1 day' where id=@id", new { id = written.PendingId });
        Assert.Equal(0, await Recovery().RunBatchAsync(fx.TenantId, default));
        Assert.True((await Recovery().RunAsync(fx.TenantId, written.PendingId.Value, true, default)).Resolved);
    }

    private sealed class PausingRepository(IRetentionJobRepository inner) : IRetentionJobRepository
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<int> RecalculateAsync(Guid t, int days, CancellationToken ct) => inner.RecalculateAsync(t, days, ct);
        public Task<RetentionDashboardVM> GetDashboardAsync(Guid t, int days, CancellationToken ct) => inner.GetDashboardAsync(t, days, ct);
        public Task<int> RecalculateOneAsync(Guid t, Guid d, int days, CancellationToken ct) => inner.RecalculateOneAsync(t, d, days, ct);
        public async Task<int> RecalculateOneAsync(System.Data.IDbConnection c, System.Data.IDbTransaction tx, Guid t, Guid d, int days, CancellationToken ct)
        {
            Started.TrySetResult();
            await Continue.Task.WaitAsync(ct);
            return await inner.RecalculateOneAsync(c, tx, t, d, days, ct);
        }
        public Task<Guid> EnqueueRecalculateAsync(Guid tenantId, Guid documentId, string reason, CancellationToken ct) => inner.EnqueueRecalculateAsync(tenantId, documentId, reason, ct);
        public Task<Guid> EnqueueRecalculateAsync(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, Guid tenantId, Guid documentId, string reason, CancellationToken ct) => inner.EnqueueRecalculateAsync(connection, transaction, tenantId, documentId, reason, ct);
        public Task<bool> ResolvePendingRecalcAsync(Guid tenantId, Guid pendingId, CancellationToken ct) => inner.ResolvePendingRecalcAsync(tenantId, pendingId, ct);
        public Task<bool> ResolvePendingRecalcAsync(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, Guid tenantId, Guid pendingId, CancellationToken ct) => inner.ResolvePendingRecalcAsync(connection, transaction, tenantId, pendingId, ct);
    }
    private NpgsqlConnectionFactory Factory() => new(PgGate.Dsn());
    private async Task<T> One<T>(string sql, object args) => await _admin!.ExecuteScalarAsync<T>(sql, args);

    private async Task<Fixture> Seed()
    {
        var fx = new Fixture(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await _admin!.ExecuteAsync("""
insert into ged.tenant(id,name,code) values(@TenantId,'ciclo',@TenantId::text) on conflict (id) do nothing;
insert into ged.app_user(id,tenant_id,name,email,password_hash) values(@ReviewerId,@TenantId,'Reviewer',@ReviewerId::text || '@test.local','hash') on conflict (id) do nothing;
insert into ged.ai_execution(id,tenant_id,user_id,task,provider,model,idempotency_key,input_fingerprint,policy_revision,state,reserved_tokens,reservation_period,expires_at,source_documents,document_refs)
values(@ExecutionId,@TenantId,@ReviewerId,'SuggestArchivalClassification','test','test',@ExecutionId::text,'fingerprint',1,'Completed',1,current_date,now()+interval '1 day','[]'::jsonb,'[]'::jsonb);
insert into ged.document(id,tenant_id,code,title,description,is_confidential,current_version_id,retention_hold,created_at,reg_status,status)
values(@DocumentId,@TenantId,'DOC-CYCLE','Antes','Antes',false,@VersionId,true,now(),'A','ACTIVE');
insert into ged.document_version(id,tenant_id,document_id,version_number,file_name,file_extension,file_size_bytes,storage_path) values(@VersionId,@TenantId,@DocumentId,1,'doc1.pdf','.pdf',1024,'docs/doc1.pdf'),(@LaterVersionId,@TenantId,@DocumentId,2,'doc2.pdf','.pdf',2048,'docs/doc2.pdf');
insert into ged.document_type(id,tenant_id,code,name,reg_status) values(@TypeId,@TenantId,'DT-' || substr(@TypeId::text, 1, 8),'Contrato','A');
insert into ged.classification_plan(id,tenant_id,code,name,retention_start_event,retention_active_days,retention_active_months,retention_active_years,retention_archive_days,retention_archive_months,retention_archive_years)
values(@ClassId,@TenantId,'CP-' || substr(@ClassId::text, 1, 8),'Classe Geral','ABERTURA',0,0,1,0,0,0);
insert into ged.classification_plan_version(id,tenant_id,version_no,title) values(@PlanVersionId,@TenantId,1,'V1');
insert into ged.classification_plan_version_item(tenant_id,version_id,classification_id,code,name,is_active,retention_start_event,retention_active_days,retention_active_months,retention_active_years,retention_archive_days,retention_archive_months,retention_archive_years,final_destination,requires_digital_signature,is_confidential) values(@TenantId,@PlanVersionId,@ClassId,'01.02','Classe',true,'ABERTURA',0,0,1,0,0,0,'ELIMINAR',false,false);
insert into ged.loan_request(id,tenant_id,document_id,requester_id,requested_at,due_at,status,notes,reg_status) values(@LoanId,@TenantId,@DocumentId,@ReviewerId,now(),now()+interval '7 days','REQUESTED','LOAN','A');
""", fx);
        fx.Token = await One<long>("select xmin::text::bigint from ged.document where id=@id", new { id = fx.DocumentId });
        return fx;
    }

    private static AssistedApplicationRecord Record(Fixture fx, string outcome, bool queue, string? decision = null)
    {
        decision ??= ReviewIdentity.CatalogJson(AiTask.SuggestArchivalClassification.ToString(), fx.ClassId);
        return new(fx.TenantId, fx.ExecutionId, fx.DocumentId, fx.VersionId, AiTask.SuggestArchivalClassification.ToString(), fx.ReviewerId, ReviewIdentity.Fingerprint(decision), decision, outcome, queue, "AI_ARCHIVAL_APPLY", "Classificação de teste", "{}", "127.0.0.1", "test", fx.ExecutionId.ToString("N"), queue, ReviewIdentity.OperationKey(fx.TenantId, fx.ExecutionId, fx.DocumentId, fx.VersionId, fx.ReviewerId, AiTask.SuggestArchivalClassification.ToString()));
    }

    private static AssistedApplicationRecord TypeRecord(Fixture fx)
    {
        var decision = ReviewIdentity.CatalogJson(AiTask.SuggestClassification.ToString(), fx.TypeId);
        return new(fx.TenantId, fx.ExecutionId, fx.DocumentId, fx.VersionId, AiTask.SuggestClassification.ToString(), fx.ReviewerId, ReviewIdentity.Fingerprint(decision), decision, "Applied", true, "AI_CLASSIFICATION_APPLY", "Tipo de teste", "{}", null, null, null, true, ReviewIdentity.OperationKey(fx.TenantId, fx.ExecutionId, fx.DocumentId, fx.VersionId, fx.ReviewerId, AiTask.SuggestClassification.ToString()));
    }

    private sealed record Fixture(Guid TenantId, Guid ReviewerId, Guid ExecutionId, Guid DocumentId, Guid VersionId, Guid LaterVersionId, Guid TypeId, Guid ClassId, Guid PlanVersionId, Guid NextPlanVersionId, Guid LoanId)
    {
        public long Token { get; set; }
    }

    private const string Schema = """
create schema if not exists ged;
create extension if not exists pgcrypto;
create table if not exists ged.tenant(id uuid primary key, name text);
alter table ged.tenant add column if not exists code varchar(50);
alter table ged.ai_execution add column if not exists source_documents jsonb not null default '[]'::jsonb;
alter table ged.ai_suggestion_application add column if not exists operation_key varchar(64);
create unique index if not exists ux_ai_suggestion_application_operation on ged.ai_suggestion_application(tenant_id, operation_key) where operation_key is not null;
alter table ged.ai_retention_recalc_pending add column if not exists attempts integer not null default 0;
alter table ged.ai_retention_recalc_pending add column if not exists last_error varchar(200);
alter table ged.ai_retention_recalc_pending add column if not exists claimed_at timestamptz;
alter table ged.ai_retention_recalc_pending add column if not exists claim_token uuid;
alter table ged.ai_retention_recalc_pending add column if not exists next_attempt_at timestamptz not null default now();
create table if not exists ged.document(
  id uuid primary key, tenant_id uuid not null, title text, description text, is_confidential boolean not null default false,
  type_id uuid, classification_id uuid, classification_version_id uuid, current_version_id uuid, retention_hold boolean not null default false,
  retention_basis_at timestamptz, retention_due_at timestamptz, retention_status text, created_at timestamptz, archived_at timestamptz, closed_at timestamptz,
  updated_at timestamptz, updated_by uuid, reg_status char(1) not null default 'A', status text not null default 'ACTIVE');
create table if not exists ged.document_version(id uuid primary key, tenant_id uuid not null, document_id uuid not null, version_number int not null);
create table if not exists ged.document_type(id uuid primary key, tenant_id uuid not null, name text not null, reg_status char(1) not null default 'A');
create table if not exists ged.document_classification(
  document_id uuid primary key, tenant_id uuid not null, document_version_id uuid, document_type_id uuid, confidence numeric, method text, summary text,
  classified_at timestamptz, classified_by uuid, source text, updated_at timestamptz, reg_status char(1) not null default 'A');
create table if not exists ged.classification_plan(
  id uuid primary key, tenant_id uuid not null, retention_start_event text, retention_active_days int not null default 0, retention_active_months int not null default 0,
  retention_active_years int not null default 0, retention_archive_days int not null default 0, retention_archive_months int not null default 0, retention_archive_years int not null default 0);
create table if not exists ged.classification_plan_version(id uuid primary key, tenant_id uuid not null, version_no int not null);
create table if not exists ged.classification_plan_version_item(tenant_id uuid not null, version_id uuid not null, classification_id uuid not null, code text, name text, is_active boolean not null default true);
create table if not exists ged.loan_request(id uuid primary key, tenant_id uuid not null, document_id uuid not null, notes text, reg_status char(1) not null default 'A');
create table if not exists ged.app_audit_log(
  id uuid primary key, tenant_id uuid, user_id uuid, user_name text, action text not null, event_type text not null default 'INFO', source text, entity_name text,
  entity_id text, message text, details jsonb, correlation_id text, ip_address text, user_agent text, created_at timestamptz not null default now(), reg_status char(1) not null default 'A');
""";
}
