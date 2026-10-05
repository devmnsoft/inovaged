using Dapper;
using InovaGed.Application.ArtificialIntelligence;
using InovaGed.Application.Common.Database;

namespace InovaGed.Infrastructure.ArtificialIntelligence;

/// <summary>Canonical assisted writes. Tags, metadata, holds and loans that are outside the review stay untouched.</summary>
public sealed class PostgresAssistedDocumentStore(IDbConnectionFactory db) : IAssistedDocumentStore
{
    public async Task<AssistedWriteResult> ApplyMetadataAsync(AssistedApplicationRecord application, long concurrencyToken, string title, string? description, bool confidential, bool mutate, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        return await AssistedApplicationWrite.ExecuteAsync(connection, application, concurrencyToken, mutate, async (c, tx) =>
            await c.ExecuteAsync(new CommandDefinition("""
update ged.document
set title=@Title, description=@Description, is_confidential=@Confidential, updated_at=now(), updated_by=@UserId
where tenant_id=@TenantId and id=@DocumentId and xmin::text::bigint=@Token
""", new { application.TenantId, application.DocumentId, UserId = application.ReviewerId, Title = title, Description = description, Confidential = confidential, Token = concurrencyToken }, tx, cancellationToken: ct)), ct);
    }

    public async Task<AssistedWriteResult> ApplyDocumentTypeAsync(AssistedApplicationRecord application, long concurrencyToken, Guid typeId, bool mutate, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        return await AssistedApplicationWrite.ExecuteAsync(connection, application, concurrencyToken, mutate, async (c, tx) =>
        {
            var rows = await c.ExecuteAsync(new CommandDefinition("""
update ged.document set type_id=@TypeId, updated_at=now(), updated_by=@UserId
where tenant_id=@TenantId and id=@DocumentId and xmin::text::bigint=@Token and current_version_id=@VersionId
""", new { application.TenantId, application.DocumentId, UserId = application.ReviewerId, TypeId = typeId, Token = concurrencyToken, VersionId = application.VersionId }, tx, cancellationToken: ct));
            if (rows == 0) return 0;
            await c.ExecuteAsync(new CommandDefinition("""
insert into ged.document_classification(document_id,tenant_id,document_version_id,document_type_id,confidence,method,summary,classified_at,classified_by,source,updated_at,reg_status)
values(@DocumentId,@TenantId,@VersionId,@TypeId,null,'MANUAL',null,now(),@UserId,'WEB',now(),'A')
on conflict (document_id) do update set document_version_id=excluded.document_version_id, document_type_id=excluded.document_type_id,
  confidence=null, method='MANUAL', classified_at=now(), classified_by=excluded.classified_by, source='WEB', updated_at=now(), reg_status='A'
""", new { application.TenantId, application.DocumentId, VersionId = application.VersionId, TypeId = typeId, UserId = application.ReviewerId }, tx, cancellationToken: ct));
            return rows;
        }, ct);
    }

    public async Task<AssistedWriteResult> ApplyArchivalClassAsync(AssistedApplicationRecord application, long concurrencyToken, Guid classificationId, bool mutate, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        return await AssistedApplicationWrite.ExecuteAsync(connection, application, concurrencyToken, mutate, async (c, tx) =>
            await c.ExecuteAsync(new CommandDefinition("""
update ged.document d
set classification_id=@ClassificationId, classification_version_id=v.id, updated_at=now(), updated_by=@UserId
from ged.classification_plan_version v
where d.tenant_id=@TenantId and d.id=@DocumentId and d.xmin::text::bigint=@Token and d.current_version_id=@VersionId
  and v.tenant_id=@TenantId
  and v.id=(select id from ged.classification_plan_version where tenant_id=@TenantId order by version_no desc limit 1)
  and exists (
    select 1 from ged.classification_plan_version_item i
    where i.tenant_id=@TenantId and i.version_id=v.id and i.classification_id=@ClassificationId and coalesce(i.is_active,true))
""", new { application.TenantId, application.DocumentId, UserId = application.ReviewerId, ClassificationId = classificationId, Token = concurrencyToken, VersionId = application.VersionId }, tx, cancellationToken: ct)), ct);
    }

    public async Task<StoredReview?> FindReviewAsync(Guid tenantId, string operationKey, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<ReviewLookup>(new CommandDefinition("""
select a.id "Id", a.decision_fingerprint "Fingerprint", a.decision_json::text "DecisionJson", a.outcome "Outcome", a.partial "Partial",
       coalesce(p.open, false) "RetentionPending", coalesce(p.attempts, 0) "RetentionAttempts", p.resolved_at "RetentionResolvedAt"
from ged.ai_suggestion_application a
left join lateral (
  select resolved_at is null as open, attempts, resolved_at
  from ged.ai_retention_recalc_pending
  where tenant_id=a.tenant_id and application_id=a.id
  order by created_at desc limit 1
) p on true
where a.tenant_id=@tenantId and a.operation_key=@operationKey
""", new { tenantId, operationKey }, cancellationToken: ct));
        return row is null ? null : new StoredReview(row.Id, row.Fingerprint, row.DecisionJson, row.Outcome, row.Partial, row.RetentionPending, row.RetentionAttempts, row.RetentionResolvedAt);
    }

    public async Task<RetentionClaim?> ClaimRetentionAsync(Guid tenantId, Guid pendingId, Guid claimToken, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<ClaimRow>(new CommandDefinition("""
with candidate as (
  select id from ged.ai_retention_recalc_pending
  where id=@pendingId and tenant_id=@tenantId and resolved_at is null
    and (claimed_at is null or claimed_at < now() - interval '90 seconds')
  for update skip locked
)
update ged.ai_retention_recalc_pending p
set claimed_at=now(), claim_token=@claimToken, attempts=p.attempts+1
from candidate c
where p.id=c.id
returning p.id "Id", p.document_id "DocumentId", p.application_id "ApplicationId", p.attempts "Attempts"
""", new { tenantId, pendingId, claimToken }, cancellationToken: ct));
        return row is null ? null : new RetentionClaim(row.Id, row.DocumentId, row.ApplicationId, row.Attempts);
    }

    public async Task<bool> ResolveRetentionAsync(Guid tenantId, Guid pendingId, Guid claimToken, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        var applicationId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
update ged.ai_retention_recalc_pending
set resolved_at=now(), claimed_at=null, claim_token=null, last_error=null
where id=@pendingId and tenant_id=@tenantId and claim_token=@claimToken and resolved_at is null
returning application_id
""", new { tenantId, pendingId, claimToken }, cancellationToken: ct));
        if (applicationId is null) return false;
        await connection.ExecuteAsync(new CommandDefinition("update ged.ai_suggestion_application set partial=false where id=@applicationId and tenant_id=@tenantId", new { applicationId, tenantId }, cancellationToken: ct));
        return true;
    }

    public async Task FailRetentionAsync(Guid tenantId, Guid pendingId, Guid claimToken, string error, CancellationToken ct)
    {
        var sanitized = Sanitize(error);
        await using var connection = await db.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
update ged.ai_retention_recalc_pending
set claimed_at=null, claim_token=null, last_error=@sanitized
where id=@pendingId and tenant_id=@tenantId and claim_token=@claimToken and resolved_at is null
""", new { tenantId, pendingId, claimToken, sanitized }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ReviewHistoryRow>> ListReviewsAsync(Guid tenantId, Guid documentId, int offset, int limit, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        var rows = await connection.QueryAsync<ReviewHistoryRow>(new CommandDefinition("""
select created_at "CreatedAt", id "Id", kind "Kind", task "Task", document_id "DocumentId", version_id "VersionId", reviewer_id "ReviewerId",
       outcome "Outcome", partial "Partial", decision_json "DecisionJson", result_expires_at "ResultExpiresAt", execution_state "ExecutionState",
       pending_id "PendingId", resolved_at "ResolvedAt", attempts "Attempts", last_error "LastError", count(*) over() "Total"
from (
  select a.created_at, a.id, 'application'::text kind, a.task, a.document_id, a.version_id, a.reviewer_id, a.outcome, a.partial, a.decision_json::text decision_json,
         e.result_expires_at, e.state execution_state, p.id pending_id, p.resolved_at, coalesce(p.attempts,0) attempts, p.last_error
  from ged.ai_suggestion_application a
  join ged.ai_execution e on e.id=a.execution_id and e.tenant_id=a.tenant_id
  left join lateral (
    select id, resolved_at, attempts, last_error from ged.ai_retention_recalc_pending
    where tenant_id=a.tenant_id and application_id=a.id order by created_at desc limit 1
  ) p on true
  where a.tenant_id=@tenantId and a.document_id=@documentId
  union all
  select e.created_at, e.id, 'suggestion', e.task, @documentId, null::uuid, e.user_id, null::text, false, null::text,
         e.result_expires_at, e.state, null::uuid, null::timestamptz, 0, null::varchar
  from ged.ai_execution e
  where e.tenant_id=@tenantId
    and jsonb_typeof(coalesce(e.source_documents,'[]'::jsonb))='array'
    and exists (
      select 1 from jsonb_array_elements(e.source_documents) s
      where jsonb_typeof(s)='object' and replace(coalesce(s->>'documentId',''),'-','') = replace(@documentId::text,'-','')
    )
    and not exists (select 1 from ged.ai_suggestion_application a where a.tenant_id=e.tenant_id and a.execution_id=e.id and a.document_id=@documentId)
) events
order by created_at desc, id desc
limit @limit offset @offset
""", new { tenantId, documentId, limit, offset }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<RetentionPendingItem>> ListRetentionAsync(Guid tenantId, Guid documentId, bool includeResolved, int offset, int limit, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        var rows = await connection.QueryAsync<PendingItemRow>(new CommandDefinition("""
select id "Id", document_id "DocumentId", created_at "CreatedAt", attempts "Attempts", last_error "LastError", resolved_at "ResolvedAt",
       claimed_at "ClaimedAt"
from ged.ai_retention_recalc_pending
where tenant_id=@tenantId and document_id=@documentId and (@includeResolved or resolved_at is null)
order by created_at desc, id desc
limit @limit offset @offset
""", new { tenantId, documentId, includeResolved, limit, offset }, cancellationToken: ct));
        return rows.Select(MapPending).ToList();
    }

    public async Task<RetentionPendingItem?> GetRetentionAsync(Guid tenantId, Guid pendingId, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<PendingItemRow>(new CommandDefinition("""
select id "Id", document_id "DocumentId", created_at "CreatedAt", attempts "Attempts", last_error "LastError", resolved_at "ResolvedAt", claimed_at "ClaimedAt"
from ged.ai_retention_recalc_pending
where tenant_id=@tenantId and id=@pendingId
""", new { tenantId, pendingId }, cancellationToken: ct));
        return row is null ? null : MapPending(row);
    }

    private static RetentionPendingItem MapPending(PendingItemRow row)
    {
        var state = row.ResolvedAt is not null ? "resolvida" : row.ClaimedAt is not null && row.ClaimedAt > DateTimeOffset.UtcNow.AddSeconds(-90) ? "em processamento" : "pendente";
        return new(row.Id, row.DocumentId, row.CreatedAt, state, row.Attempts, row.LastError, row.ResolvedAt);
    }

    private static string Sanitize(string error)
    {
        var text = error ?? "";
        if (text.Contains("cancel", StringComparison.OrdinalIgnoreCase)) return "temporalidade:cancelada";
        if (text.Length > 80) text = text[..80];
        foreach (var ch in text) { if (char.IsLetterOrDigit(ch) || ch is ':' or '_' or '-') continue; return "temporalidade:falha"; }
        return string.IsNullOrWhiteSpace(text) ? "temporalidade:falha" : text;
    }

    private sealed class ReviewLookup { public Guid Id { get; set; } public string Fingerprint { get; set; } = ""; public string DecisionJson { get; set; } = ""; public string Outcome { get; set; } = ""; public bool Partial { get; set; } public bool RetentionPending { get; set; } public int RetentionAttempts { get; set; } public DateTimeOffset? RetentionResolvedAt { get; set; } }
    private sealed class ClaimRow { public Guid Id { get; set; } public Guid DocumentId { get; set; } public Guid? ApplicationId { get; set; } public int Attempts { get; set; } }
    private sealed class PendingItemRow { public Guid Id { get; set; } public Guid DocumentId { get; set; } public DateTimeOffset CreatedAt { get; set; } public int Attempts { get; set; } public string? LastError { get; set; } public DateTimeOffset? ResolvedAt { get; set; } public DateTimeOffset? ClaimedAt { get; set; } }
}
