using InovaGed.Domain.Primitives;

namespace InovaGed.Application.Ged.Instruments;

public sealed class PopProcedureRow
{
    public Guid Id { get; init; }
    public string Code { get; init; } = "";
    public string Title { get; init; } = "";
    public string ContentMd { get; init; } = "";
    public bool IsActive { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public Guid? CreatedBy { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public Guid? UpdatedBy { get; init; }
}

public class PopProcedureCreateVM
{
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string ContentMd { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public sealed class PopProcedureUpdateVM : PopProcedureCreateVM { }

public sealed class PublishPopVersionVM
{
    public Guid ProcedureId { get; set; }
    public string Title { get; set; } = "";
    public string ContentMd { get; set; } = "";
    public string? Notes { get; set; }
}

public interface IPopProcedureCommands
{
    Task<Result<Guid>> CreateAsync(Guid tenantId, Guid? userId, PopProcedureCreateVM vm, CancellationToken ct);
    Task<Result> UpdateAsync(Guid tenantId, Guid id, Guid? userId, PopProcedureUpdateVM vm, CancellationToken ct);
    Task<Result<Guid>> PublishVersionAsync(Guid tenantId, Guid? userId, PublishPopVersionVM vm, CancellationToken ct);
}

public interface IPopProcedureQueries
{
    Task<IReadOnlyList<PopProcedureRow>> ListAsync(Guid tenantId, CancellationToken ct);
    Task<IReadOnlyList<PopProcedureVersionRow>> ListVersionsAsync(Guid tenantId, Guid procedureId, CancellationToken ct);
}

public sealed class PopProcedureVersionRow
{
    public Guid Id { get; init; }
    public int VersionNo { get; init; }
    public string Title { get; init; } = "";
    public DateTimeOffset PublishedAt { get; init; }
    public Guid? PublishedBy { get; init; }
    public string? Notes { get; init; }
}