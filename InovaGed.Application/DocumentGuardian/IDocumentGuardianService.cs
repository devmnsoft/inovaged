namespace InovaGed.Application.DocumentGuardian;

public interface IDocumentGuardianService
{
    Task<DocumentGuardianViewModel?> GetAsync(Guid tenantId, Guid userId, Guid documentId, string correlationId, CancellationToken ct);
}
