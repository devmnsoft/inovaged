using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Documents;

namespace InovaGed.Infrastructure.Ged.Documents;

/// <summary>Official, tenant-scoped persistence boundary for the intake checklist.</summary>
public sealed class DocumentIntakeReviewService(IDbConnectionFactory db, IAuditWriter audit)
    : IDocumentIntakeReviewService
{
    public async Task<DocumentIntakeReviewDto> GetAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        var rows = await GetForDocumentsAsync(tenantId, new[] { documentId }, ct);
        return rows[documentId];
    }

    public async Task<IReadOnlyDictionary<Guid, DocumentIntakeReviewDto>> GetForDocumentsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> documentIds, CancellationToken ct)
    {
        var ids = documentIds.Where(x => x != Guid.Empty).Distinct().Take(500).ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, DocumentIntakeReviewDto>();
        await using var connection = await db.OpenAsync(ct);
        var existing = (await connection.QueryAsync<DocumentIntakeReviewDto>(new CommandDefinition("""
select d.id as DocumentId, coalesce(r.status, 'PENDING') as Status,
       r.reviewed_by as ReviewedBy, r.reviewed_at as ReviewedAt, r.notes as Notes
from ged.document d
left join ged.document_intake_review r on r.tenant_id=d.tenant_id and r.document_id=d.id and r.reg_status='A'
where d.tenant_id=@tenantId and d.id=any(@ids) and coalesce(d.reg_status,'A')='A';
""", new { tenantId, ids }, cancellationToken: ct))).ToDictionary(x => x.DocumentId);
        foreach (var id in ids)
            if (!existing.ContainsKey(id)) throw new KeyNotFoundException("Documento não encontrado ou inacessível.");
        return existing;
    }

    public Task<DocumentIntakeReviewDto> MarkReviewedAsync(Guid tenantId, Guid userId, Guid documentId,
        string? notes, CancellationToken ct) => ChangeAsync(tenantId, userId, documentId,
            DocumentIntakeReviewStatus.Reviewed, string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(), "INTAKE_REVIEWED", ct);

    public Task<DocumentIntakeReviewDto> MarkNeedsCorrectionAsync(Guid tenantId, Guid userId,
        Guid documentId, string reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("O motivo é obrigatório.", nameof(reason));
        return ChangeAsync(tenantId, userId, documentId, DocumentIntakeReviewStatus.NeedsCorrection,
            reason.Trim(), "INTAKE_NEEDS_CORRECTION", ct);
    }

    public Task<DocumentIntakeReviewDto> ResetPendingAsync(Guid tenantId, Guid userId, Guid documentId,
        CancellationToken ct) => ChangeAsync(tenantId, userId, documentId,
            DocumentIntakeReviewStatus.Pending, null, "INTAKE_RESET_PENDING", ct);

    private async Task<DocumentIntakeReviewDto> ChangeAsync(Guid tenantId, Guid userId, Guid documentId,
        string status, string? notes, string auditEvent, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        var result = await connection.QuerySingleOrDefaultAsync<DocumentIntakeReviewDto>(new CommandDefinition("""
insert into ged.document_intake_review(tenant_id, document_id, status, reviewed_by, reviewed_at, notes)
select @tenantId, d.id, @status,
       case when @status='PENDING' then null else @userId end,
       case when @status='PENDING' then null else now() end, @notes
from ged.document d
where d.tenant_id=@tenantId and d.id=@documentId and coalesce(d.reg_status,'A')='A'
on conflict (tenant_id, document_id) where reg_status='A' do update
set status=excluded.status, reviewed_by=excluded.reviewed_by,
    reviewed_at=excluded.reviewed_at, notes=excluded.notes
returning document_id as DocumentId, status as Status, reviewed_by as ReviewedBy,
          reviewed_at as ReviewedAt, notes as Notes;
""", new { tenantId, userId, documentId, status, notes }, cancellationToken: ct));
        if (result is null) throw new KeyNotFoundException("Documento não encontrado ou inacessível.");
        await audit.WriteAsync(tenantId, userId, auditEvent, "DOCUMENT_INTAKE_REVIEW", documentId,
            "Conferência documental atualizada", null, null, new { status }, ct);
        return result;
    }
}
