using System;
using System.Collections.Generic;

namespace InovaGed.Web.Models.Classification;

public sealed class ClassificationDashboardVM
{
    public Guid? FolderId { get; set; }

    public int TotalPending { get; set; }

    public List<ByFolderVM> ByFolder { get; set; } = new();

    public List<ItemVM> Items { get; set; } = new();

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    public sealed class ByFolderVM
    {
        public Guid FolderId { get; set; }
        public string FolderName { get; set; } = "";
        public int Count { get; set; }
    }

    public sealed class ItemVM
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = "";
        public string FolderName { get; set; } = "";
        public string FileName { get; set; } = "";
        public DateTimeOffset CreatedAt { get; set; }
    }
}
