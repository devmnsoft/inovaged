using InovaGed.Domain.Primitives;

namespace InovaGed.Domain.Auditing;

public sealed class AuditLog : TenantEntity
{
    public string Action { get; private set; } = default!;         // DOC_UPLOADED, DOC_DOWNLOADED...
    public string EntityName { get; private set; } = default!;     // Document, Folder...
    public Guid? EntityId { get; private set; }
    public string? DataJson { get; private set; }
    public string? Ip { get; private set; }
    public string? UserAgent { get; private set; }

    private AuditLog() { }

    public AuditLog(Guid tenantId, string action, string entityName, Guid? entityId, string? dataJson,
        Guid userId, string? ip, string? userAgent)
    {
        TenantId = tenantId;
        Action = action;
        EntityName = entityName;
        EntityId = entityId;
        DataJson = dataJson;
        CreatedBy = userId;
        Ip = ip;
        UserAgent = userAgent;
    }
}
