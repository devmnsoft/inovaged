namespace InovaGed.Application.Search
{ 
    public sealed record DocumentSearchRowDto(
        Guid DocumentId,
        Guid VersionId,
        string Code,
        string Title,
        string FileName,
        string Snippet,
        float Rank
    );

    public interface IDocumentSearchQueries
    {
        Task<IReadOnlyList<DocumentSearchRowDto>> SearchAsync(
            Guid tenantId,
            string q,
            Guid? folderId,
            int limit,
            CancellationToken ct);
    }

}
