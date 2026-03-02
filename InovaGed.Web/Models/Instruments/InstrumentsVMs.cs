using InovaGed.Application.Instruments;

namespace InovaGed.Web.Models.Instruments;

public sealed class InstrumentsIndexVM
{
    public string Type { get; set; } = "PCD";
    public IReadOnlyList<InstrumentVersionRow> Versions { get; set; } = Array.Empty<InstrumentVersionRow>();
}

public sealed class InstrumentsNodesVM
{
    public string Type { get; set; } = "PCD";
    public Guid VersionId { get; set; }

    public IReadOnlyList<InstrumentVersionRow> Versions { get; set; } = Array.Empty<InstrumentVersionRow>();
    public IReadOnlyList<InstrumentNodeRow> Nodes { get; set; } = Array.Empty<InstrumentNodeRow>();

    public InstrumentNodeRow? Edit { get; set; }
}

public sealed class InstrumentsPrintVM
{
    public string Type { get; set; } = "PCD";
    public Guid VersionId { get; set; }
    public string Html { get; set; } = "";
}

public sealed class InstrumentsNodeUpsertRequest
{
    public string Type { get; set; } = "PCD";
    public Guid VersionId { get; set; }

    public Guid? Id { get; set; }
    public Guid? ParentId { get; set; }

    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }

    public int SortOrder { get; set; } = 0;
    public string SecurityLevel { get; set; } = "PUBLIC";
}

public sealed class InstrumentsMoveRequest
{
    public string Type { get; set; } = "PCD";
    public Guid VersionId { get; set; }

    public Guid NodeId { get; set; }
    public Guid? NewParentId { get; set; }
    public int NewSortOrder { get; set; }
}