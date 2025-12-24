using InovaGed.Domain.Primitives;

namespace InovaGed.Domain.Security
{
    public sealed class AppRole : TenantEntity
    {
        public string Name { get; private set; } = default!;
        public string NormalizedName { get; private set; } = default!;

        private AppRole() { }

        public AppRole(Guid tenantId, string name, Guid createdBy)
        {
            TenantId = tenantId;
            Name = name.Trim();
            NormalizedName = Name.ToUpperInvariant();
            CreatedBy = createdBy;
        }
    }
}
