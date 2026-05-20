namespace InovaGed.Application.Security;

public interface IAbacAuthorizationService
{
    Task<bool> CanAccessDocumentAsync(Guid tenantId, Guid userId, Guid documentId, string action, IReadOnlyDictionary<string, string> attributes, CancellationToken ct);
}
