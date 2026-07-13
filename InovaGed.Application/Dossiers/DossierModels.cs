namespace InovaGed.Application.Dossiers;
public sealed record DossierDto(Guid Id, Guid TenantId, string DossierType, string Title, decimal CompletenessScore, decimal RiskScore, string Status);
public sealed record DossierDocumentDto(Guid DossierId, Guid DocumentId, string InclusionMode, string? RuleCode, decimal Confidence, string Status);
public interface IDossierService
{
    Task<DossierDto> CreateAsync(Guid tenantId, string dossierType, string title, Guid userId, CancellationToken ct);
    Task AddDocumentAsync(Guid tenantId, Guid dossierId, Guid documentId, string inclusionMode, string? ruleCode, decimal confidence, Guid userId, CancellationToken ct);
}
