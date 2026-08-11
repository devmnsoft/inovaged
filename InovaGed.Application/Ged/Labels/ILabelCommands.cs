namespace InovaGed.Application.Ged.Labels;
public interface ILabelCommands
{
    Task<Guid> SaveAsync(Guid tenantId, Guid userId, LabelFormDto label, CancellationToken ct);
    Task InactivateAsync(Guid tenantId, Guid userId, Guid id, CancellationToken ct);
}
