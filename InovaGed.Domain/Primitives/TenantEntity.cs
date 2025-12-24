namespace InovaGed.Domain.Primitives;

public abstract class TenantEntity : Entity
{
    public Guid TenantId { get; protected set; }

    public DateTime CreatedAtUtc { get; protected set; } = DateTime.UtcNow;
    public Guid? CreatedBy { get; protected set; }

    public DateTime? UpdatedAtUtc { get; protected set; }
    public Guid? UpdatedBy { get; protected set; }

    public DateTime? DeletedAtUtc { get; protected set; }
    public Guid? DeletedBy { get; protected set; }
    public bool IsDeleted => DeletedAtUtc.HasValue;

    public void MarkDeleted(Guid userId)
    {
        DeletedAtUtc = DateTime.UtcNow;
        DeletedBy = userId;
    }

    public void Touch(Guid userId)
    {
        UpdatedAtUtc = DateTime.UtcNow;
        UpdatedBy = userId;
    }
}
