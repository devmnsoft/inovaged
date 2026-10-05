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
            var versionId = await c.ExecuteScalarAsync<Guid?>(new CommandDefinition("select id from ged.document_version where tenant_id=@TenantId and document_id=@DocumentId order by version_number desc limit 1", new { application.TenantId, application.DocumentId }, tx, cancellationToken: ct));
            var rows = await c.ExecuteAsync(new CommandDefinition("""
update ged.document set type_id=@TypeId, updated_at=now(), updated_by=@UserId
where tenant_id=@TenantId and id=@DocumentId and xmin::text::bigint=@Token
""", new { application.TenantId, application.DocumentId, UserId = application.ReviewerId, TypeId = typeId, Token = concurrencyToken }, tx, cancellationToken: ct));
            if (rows == 0 || versionId is null) return 0;
            await c.ExecuteAsync(new CommandDefinition("""
insert into ged.document_classification(document_id,tenant_id,document_version_id,document_type_id,confidence,method,summary,classified_at,classified_by,source,updated_at,reg_status)
values(@DocumentId,@TenantId,@VersionId,@TypeId,null,'MANUAL',null,now(),@UserId,'WEB',now(),'A')
on conflict (document_id) do update set document_version_id=excluded.document_version_id, document_type_id=excluded.document_type_id,
  confidence=null, method='MANUAL', classified_at=now(), classified_by=excluded.classified_by, source='WEB', updated_at=now(), reg_status='A'
""", new { application.TenantId, application.DocumentId, VersionId = versionId, TypeId = typeId, UserId = application.ReviewerId }, tx, cancellationToken: ct));
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
from (select id from ged.classification_plan_version where tenant_id=@TenantId order by version_no desc limit 1) v
where d.tenant_id=@TenantId and d.id=@DocumentId and d.xmin::text::bigint=@Token
""", new { application.TenantId, application.DocumentId, UserId = application.ReviewerId, ClassificationId = classificationId, Token = concurrencyToken }, tx, cancellationToken: ct)), ct);
    }

    public async Task RecordRetentionPendingAsync(Guid tenantId, Guid documentId, Guid? applicationId, string reason, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
insert into ged.ai_retention_recalc_pending(id,tenant_id,document_id,application_id,reason)
values(@Id,@TenantId,@DocumentId,@ApplicationId,@Reason)
""", new { Id = Guid.NewGuid(), TenantId = tenantId, DocumentId = documentId, ApplicationId = applicationId, Reason = reason }, cancellationToken: ct));
    }
}
