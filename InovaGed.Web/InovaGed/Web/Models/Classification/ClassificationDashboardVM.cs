using InovaGed.Application.Classification;

namespace InovaGed.Web.Models.Classification;

public sealed class ClassificationDashboardVM
{
    public Guid? FolderId { get; set; }

    public int TotalPending { get; set; }

    public List<ClassificationFolderCountDto> ByFolder { get; set; } = new();

    public List<UnclassifiedRowDto> Items { get; set; } = new();

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    public int TotalPages =>
        PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalPending / (double)PageSize));

    public bool HasPrev => Page > 1;
    public bool HasNext => Page < TotalPages;
}
