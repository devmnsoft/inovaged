using InovaGed.Domain.Primitives;

namespace InovaGed.Domain.Tenants;

public sealed class Tenant : Entity
{
    public string Name { get; private set; } = default!;
    public string Slug { get; private set; } = default!;
    public bool IsActive { get; private set; } = true;

    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    private Tenant() { }

    public Tenant(string name, string slug)
    {
        Name = name.Trim();
        Slug = slug.Trim().ToLowerInvariant();
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
