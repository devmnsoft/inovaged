namespace InovaGed.Application.Ged.Labels;
public interface ILabelQueries
{
    Task<IReadOnlyList<LabelRowDto>> ListAsync(Guid tenantId, string? search, string? type, string? status, CancellationToken ct);
    Task<LabelFormDto?> GetAsync(Guid tenantId, Guid id, CancellationToken ct);
}
