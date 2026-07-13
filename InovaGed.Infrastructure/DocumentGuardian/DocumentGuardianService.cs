using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.DocumentGuardian;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.DocumentGuardian;

public sealed class DocumentGuardianService : IDocumentGuardianService
{
    private readonly IDbConnectionFactory _db;
    private readonly IAuditWriter _audit;
    private readonly ILogger<DocumentGuardianService> _logger;

    public DocumentGuardianService(IDbConnectionFactory db, IAuditWriter audit, ILogger<DocumentGuardianService> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    public async Task<DocumentGuardianViewModel?> GetAsync(Guid tenantId, Guid userId, Guid documentId, string correlationId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var header = await conn.QuerySingleOrDefaultAsync<DocumentGuardianViewModel>(new CommandDefinition("""
select d.id as DocumentId, d.tenant_id as TenantId, coalesce(d.title, v.file_name, d.id::text) as Title,
       v.file_name as FileName, f.name as FolderName, dt.name as DocumentTypeName,
       coalesce(d.is_confidential,false) as IsConfidential,
       coalesce(t.completeness_score,0) as CompletenessScore, coalesce(t.risk_score,0) as RiskScore
from ged.document d
left join ged.document_version v on v.tenant_id=d.tenant_id and v.id=d.current_version_id
left join ged.folder f on f.tenant_id=d.tenant_id and f.id=d.folder_id
left join ged.document_type dt on dt.tenant_id=d.tenant_id and dt.id=d.type_id
left join ged.document_twin t on t.tenant_id=d.tenant_id and t.document_id=d.id and coalesce(t.reg_status,'A')='A'
where d.tenant_id=@tenantId and d.id=@documentId and coalesce(d.reg_status,'A')='A'
limit 1;
""", new { tenantId, documentId }, cancellationToken: ct));
        if (header is null) return null;

        var findings = (await conn.QueryAsync<DocumentGuardianFindingDto>(new CommandDefinition("""
select id, rule_code as RuleCode, rule_version as RuleVersion, severity, category, description, recommendation,
       confidence, status, created_at_utc as CreatedAtUtc
from ged.document_finding
where tenant_id=@tenantId and document_id=@documentId and coalesce(reg_status,'A')='A'
order by created_at_utc desc limit 100;
""", new { tenantId, documentId }, cancellationToken: ct))).AsList();

        var evidenceRows = (await conn.QueryAsync<DocumentGuardianEvidenceRow>(new CommandDefinition("""
select finding_id as FindingId, id, source_type as SourceType, evidence_key as EvidenceKey, evidence_value as EvidenceValue, excerpt, confidence
from ged.document_finding_evidence
where tenant_id=@tenantId and document_id=@documentId and coalesce(reg_status,'A')='A';
""", new { tenantId, documentId }, cancellationToken: ct))).ToLookup(x => x.FindingId);
        foreach (var f in findings)
            f.Evidences = evidenceRows[f.Id].Select(e => new DocumentGuardianEvidenceDto { Id = e.Id, SourceType = e.SourceType, EvidenceKey = e.EvidenceKey, EvidenceValue = e.EvidenceValue, Excerpt = e.Excerpt, Confidence = e.Confidence }).ToArray();

        header.Findings = findings;
        header.Relationships = (await conn.QueryAsync<DocumentGuardianRelationshipDto>(new CommandDefinition("""
select r.related_document_id as RelatedDocumentId, r.relationship_type as RelationshipType, d.title as RelatedTitle, r.confidence
from ged.document_relationship r
left join ged.document d on d.tenant_id=r.tenant_id and d.id=r.related_document_id
where r.tenant_id=@tenantId and r.document_id=@documentId and coalesce(r.reg_status,'A')='A'
order by r.created_at_utc desc limit 100;
""", new { tenantId, documentId }, cancellationToken: ct))).AsList();
        header.Obligations = (await conn.QueryAsync<DocumentGuardianObligationDto>(new CommandDefinition("""
select id, obligation_type as ObligationType, due_at_utc as DueAtUtc, status, description
from ged.document_obligation where tenant_id=@tenantId and document_id=@documentId and coalesce(reg_status,'A')='A' order by due_at_utc nulls last limit 100;
""", new { tenantId, documentId }, cancellationToken: ct))).AsList();
        header.Decisions = (await conn.QueryAsync<DocumentGuardianDecisionDto>(new CommandDefinition("""
select id, finding_id as FindingId, decision, justification, decided_at_utc as DecidedAtUtc, decided_by_name as DecidedByName
from ged.document_finding_decision where tenant_id=@tenantId and document_id=@documentId order by decided_at_utc desc limit 100;
""", new { tenantId, documentId }, cancellationToken: ct))).AsList();
        header.Timeline = (await conn.QueryAsync<DocumentGuardianTimelineEventDto>(new CommandDefinition("""
select created_at as EventAtUtc, action as EventType, 'app_audit_log' as Source, summary as Summary from ged.app_audit_log where tenant_id=@tenantId and entity_id=@documentId
union all
select created_at_utc as EventAtUtc, 'GUARDIAN_FINDING' as EventType, 'document_finding' as Source, description as Summary from ged.document_finding where tenant_id=@tenantId and document_id=@documentId
order by EventAtUtc desc limit 200;
""", new { tenantId, documentId }, cancellationToken: ct))).AsList();

        await _audit.WriteAsync(tenantId, userId, "DOCUMENT_GUARDIAN_VIEW", "DOCUMENT", documentId, "Acesso ao Guardião do documento", null, null, new { documentId, correlationId }, ct);
        _logger.LogInformation("DocumentGuardian viewed. Tenant={TenantId} User={UserId} Document={DocumentId} CorrelationId={CorrelationId}", tenantId, userId, documentId, correlationId);
        return header;
    }

    private sealed class DocumentGuardianEvidenceRow : DocumentGuardianEvidenceDto { public Guid FindingId { get; set; } }
}
