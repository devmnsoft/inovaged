namespace InovaGed.Application.Security;

public interface IAbacAuthorizationService
{
    Task<bool> CanAccessDocumentAsync(Guid tenantId, Guid userId, Guid documentId, string action, IReadOnlyDictionary<string, string> attributes, CancellationToken ct);

    Task<IReadOnlySet<Guid>> FilterDocumentsAsync(Guid tenantId, Guid userId, IReadOnlyCollection<Guid> documentIds, string action, CancellationToken ct) =>
        FilterDocumentsAsync(tenantId, userId, documentIds, action, new Dictionary<string, string>(), ct);

    async Task<IReadOnlySet<Guid>> FilterDocumentsAsync(Guid tenantId, Guid userId, IReadOnlyCollection<Guid> documentIds, string action, IReadOnlyDictionary<string, string> attributes, CancellationToken ct)
    {
        var allowed = new HashSet<Guid>();
        foreach (var id in documentIds)
            if (await CanAccessDocumentAsync(tenantId, userId, id, action, attributes, ct)) allowed.Add(id);
        return allowed;
    }
}
