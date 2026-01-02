// IDocumentClassificationCommands.cs
namespace InovaGed.Application.Classification;

public interface IDocumentClassificationCommands
{
    Task UpsertAsync(
        Guid documentId,
        Guid? documentTypeId,
        IReadOnlyList<string> tags,
        IReadOnlyDictionary<string, string> metadata,
        string? source,
        Guid? suggestedTypeId,
        decimal? suggestedConf,
        DateTimeOffset? suggestedAt,
        CancellationToken ct);

    Task SetSuggestionAsync(Guid documentId, Guid? suggestedTypeId, decimal? conf, DateTimeOffset? at, CancellationToken ct);
}
