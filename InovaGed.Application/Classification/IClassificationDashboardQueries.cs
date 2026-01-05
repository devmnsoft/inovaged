namespace InovaGed.Application.Classification;

public sealed class ClassificationFolderCountDto
{
    public Guid? FolderId { get; set; }
    public string FolderName { get; set; } = "(Sem pasta)";
    public int Count { get; set; }
}

public sealed class UnclassifiedRowDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string? FileName { get; set; }
    public Guid? FolderId { get; set; }
    public string? FolderName { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class ClassificationByFolderDto
{
    public Guid? FolderId { get; set; }
    public string FolderName { get; set; } = "(Sem pasta)";
    public int Count { get; set; }
}

 
public interface IClassificationDashboardQueries
{
    Task<int> CountAsync(Guid tenantId, Guid? folderId, CancellationToken ct);

    Task<IReadOnlyList<ClassificationFolderCountDto>> ByFolderAsync(Guid tenantId, CancellationToken ct);
 
     
    Task<IReadOnlyList<UnclassifiedRowDto>> ListAsync(Guid tenantId, Guid? folderId, int page, int pageSize, CancellationToken ct);
}
