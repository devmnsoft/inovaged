using System;
using System.Collections.Generic;

namespace InovaGed.Web.Models.Ged;

public sealed class GedExplorerVM
{
    public Guid? CurrentFolderId { get; set; }

    public Guid? FolderId { get; set; }
    public string? Query { get; set; }

    public List<FolderNodeVM> Folders { get; set; } = new();
    public List<DocumentRowVM> Documents { get; set; } = new();

    public sealed class FolderNodeVM
    {
        public Guid Id { get; set; }
        public Guid? ParentId { get; set; }
        public string Name { get; set; } = "";
        public int Level { get; set; } // pra indent no MVP
    }

    public sealed class DocumentRowVM
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = "";
        public string? TypeName { get; set; }
        public string? FileName { get; set; }
        public long SizeBytes { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid CreatedBy { get; set; }   // ou Guid?

        public bool IsConfidential { get; set; }
    }
}
