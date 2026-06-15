namespace InovaGed.Web.Models.GedSearch;

public sealed class GedSearchIndexVm
{
    public string? Query { get; set; }
    public string? Scope { get; set; }
    public Guid? FolderId { get; set; }
}
