namespace InovaGed.Application.Security;

public interface IAbacAuthorizationService
{
    Task<bool> CanAccessDocumentAsync(Guid tenantId, Guid userId, Guid documentId, string action, IReadOnlyDictionary<string, string> attributes, CancellationToken ct);

    async Task<IReadOnlySet<Guid>> FilterDocumentsAsync(Guid tenantId, Guid userId, IReadOnlyCollection<Guid> documentIds, string action, CancellationToken ct)
    {
        var allowed = new HashSet<Guid>();
        foreach (var id in documentIds)
            if (await CanAccessDocumentAsync(tenantId, userId, id, action, new Dictionary<string, string>(), ct)) allowed.Add(id);
        return allowed;
    }
}
