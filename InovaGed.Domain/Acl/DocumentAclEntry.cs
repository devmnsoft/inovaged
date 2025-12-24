using InovaGed.Domain.Acl;
using InovaGed.Domain.Primitives;

namespace InovaGed.Domain.Acl;

public sealed class DocumentAclEntry : TenantEntity
{
    public Guid DocumentId { get; private set; }
    public Guid? UserId { get; private set; }
    public Guid? RoleId { get; private set; }
    public AclPermission Permissions { get; private set; }

    private DocumentAclEntry() { }

    public DocumentAclEntry(Guid tenantId, Guid documentId, Guid? userId, Guid? roleId, AclPermission permissions, Guid createdBy)
    {
        TenantId = tenantId;
        DocumentId = documentId;
        UserId = userId;
        RoleId = roleId;
        Permissions = permissions;
        CreatedBy = createdBy;
    }
}
