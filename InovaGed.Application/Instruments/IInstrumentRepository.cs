namespace InovaGed.Application.Instruments
{
    public interface IInstrumentRepository
    {
        Task<IReadOnlyList<InstrumentVersionRow>> ListVersionsAsync(Guid tenantId, string type, CancellationToken ct);
        Task<Guid> PublishNewVersionAsync(Guid tenantId, Guid? userId, string? userName, string type, string? notes, CancellationToken ct);

        Task<IReadOnlyList<InstrumentNodeRow>> ListNodesAsync(Guid tenantId, string type, Guid versionId, CancellationToken ct);

        Task<Guid> UpsertNodeAsync(Guid tenantId, Guid? userId, string? userName, string type, Guid versionId,
            Guid? id, Guid? parentId, string code, string title, string? desc, int sortOrder, string securityLevel, CancellationToken ct);

        Task MoveNodeAsync(Guid tenantId, Guid? userId, string? userName, string type, Guid versionId, Guid nodeId, Guid? newParentId, int newSortOrder, CancellationToken ct);

        Task<string> RenderPrintHtmlAsync(Guid tenantId, string type, Guid versionId, CancellationToken ct);
    }
}
