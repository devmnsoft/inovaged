using InovaGed.Domain.Primitives;

namespace InovaGed.Domain.Documents
{
    public sealed class DocumentType : TenantEntity
    {
        public string Name { get; private set; } = default!;
        public string Code { get; private set; } = default!; // ex: CONTRATO, OFICIO
        public bool IsActive { get; private set; } = true;

        private DocumentType() { }

        public DocumentType(Guid tenantId, string name, string code, Guid createdBy)
        {
            TenantId = tenantId;
            Name = name.Trim();
            Code = code.Trim().ToUpperInvariant();
            CreatedBy = createdBy;
        }

        public void Deactivate(Guid userId) { IsActive = false; Touch(userId); }
    }
}
