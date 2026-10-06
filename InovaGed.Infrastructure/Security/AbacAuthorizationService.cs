using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Security;

namespace InovaGed.Infrastructure.Security;

public sealed class AbacAuthorizationService(IDbConnectionFactory db) : IAbacAuthorizationService
{
    public async Task<bool> CanAccessDocumentAsync(Guid tenantId, Guid userId, Guid documentId, string action, IReadOnlyDictionary<string, string> attributes, CancellationToken ct)
    {
        var rows = await QueryAllowedAsync(tenantId, userId, [documentId], action, ct);
        var document = rows.SingleOrDefault();
        if (document is null) return false;
        if (document.Confidential && attributes.TryGetValue("sector", out var sector) && !string.IsNullOrWhiteSpace(sector))
        {
            await using var connection = await db.OpenAsync(ct);
            return await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
select exists(select 1 from ged.documents d join ged.users u on u.tenant_id=d.tenant_id and u.id=@userId
where d.tenant_id=@tenantId and d.id=@documentId and d.setor=@sector)
""", new { tenantId, userId, documentId, sector }, cancellationToken: ct));
        }
        return true;
    }

    public async Task<IReadOnlySet<Guid>> FilterDocumentsAsync(Guid tenantId, Guid userId, IReadOnlyCollection<Guid> documentIds, string action, CancellationToken ct)
        => (await QueryAllowedAsync(tenantId, userId, documentIds, action, ct)).Select(x => x.Id).ToHashSet();

    private async Task<IReadOnlyList<DocumentAccess>> QueryAllowedAsync(Guid tenantId, Guid userId, IReadOnlyCollection<Guid> documentIds, string action, CancellationToken ct)
    {
        if (documentIds.Count == 0) return [];
        var permissionCode = action.Trim().ToUpperInvariant() switch
        {
            "VIEW" or "DOCUMENTS.VIEW" => "Documents.View",
            "EDIT" or "UPDATE" or "MANAGE" => "GED.DOCUMENTS",
            _ => action.Trim()
        };
        await using var connection = await db.OpenAsync(ct);
        // Single and batch reads use exactly the same live record policy. No caller-supplied
        // classification, tenant-wide role bypass, or cached permission result is trusted.
        return (await connection.QueryAsync<DocumentAccess>(new CommandDefinition("""
select d.id as "Id", d.is_confidential as "Confidential"
from ged.document d
join ged.app_user u on u.tenant_id=d.tenant_id and u.id=@userId
where d.tenant_id=@tenantId and d.id=any(@ids) and coalesce(d.reg_status,'A')='A'
  and coalesce(u.reg_status,'A')='A' and coalesce(u.is_active,true)
  and (not d.is_confidential or @hour between 6 and 20)
  and exists (
    select 1 from ged.role_permission rp
    join ged.role ro on ro.id=rp.role_id and ro.tenant_id=@tenantId
    join ged.app_role ar on ar.id=ro.id and ar.tenant_id=@tenantId
    join ged.user_role ur on ur.role_id=ar.id and ur.user_id=@userId
    join ged.permission p on p.code=rp.permission_code and coalesce(p.reg_status,'A')='A'
    where rp.tenant_id=@tenantId and rp.reg_status='A' and p.code=@permissionCode
  )
  and (
    (not d.is_confidential and not exists(select 1 from ged.document_acl a where a.document_id=d.id))
    or exists (
      select 1 from ged.document_acl a
      where a.document_id=d.id and a.can_read and (not @write or a.can_write)
        and (a.user_id=@userId or exists (
          select 1 from ged.user_role ur join ged.app_role r on r.id=ur.role_id
          where ur.user_id=@userId and ur.role_id=a.role_id and r.tenant_id=@tenantId))
    )
  )
""", new { tenantId, userId, ids = documentIds.ToArray(), permissionCode, write = permissionCode != "Documents.View", hour = DateTime.UtcNow.Hour }, cancellationToken: ct))).ToList();
    }

    private sealed class DocumentAccess { public Guid Id { get; set; } public bool Confidential { get; set; } }
}
