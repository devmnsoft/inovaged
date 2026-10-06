using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Security;

namespace InovaGed.Infrastructure.Security;

public sealed class AbacAuthorizationService(IDbConnectionFactory db) : IAbacAuthorizationService
{
    public async Task<bool> CanAccessDocumentAsync(Guid tenantId, Guid userId, Guid documentId, string action, IReadOnlyDictionary<string, string> attributes, CancellationToken ct)
    {
        var rows = await QueryAllowedAsync(tenantId, userId, [documentId], action, attributes, ct);
        return rows.Count > 0;
    }

    public Task<IReadOnlySet<Guid>> FilterDocumentsAsync(Guid tenantId, Guid userId, IReadOnlyCollection<Guid> documentIds, string action, CancellationToken ct)
        => FilterDocumentsAsync(tenantId, userId, documentIds, action, new Dictionary<string, string>(), ct);

    public async Task<IReadOnlySet<Guid>> FilterDocumentsAsync(Guid tenantId, Guid userId, IReadOnlyCollection<Guid> documentIds, string action, IReadOnlyDictionary<string, string> attributes, CancellationToken ct)
        => (await QueryAllowedAsync(tenantId, userId, documentIds, action, attributes, ct)).Select(x => x.Id).ToHashSet();

    public static string DocumentAccessPredicate(string alias = "d") => $"""
coalesce({alias}.reg_status,'A')='A'
and exists (
  select 1 from ged.app_user u
  where u.tenant_id={alias}.tenant_id and u.id=@userId
    and coalesce(u.reg_status,'A')='A' and coalesce(u.is_active,true)
)
and (not {alias}.is_confidential or @hour between 6 and 20)
and exists (
  select 1 from ged.role_permission rp
  join ged.role ro on ro.id=rp.role_id and ro.tenant_id=@tenantId
  join ged.app_role ar on ar.id=ro.id and ar.tenant_id=@tenantId
  join ged.user_role ur on ur.role_id=ar.id and ur.user_id=@userId
  join ged.permission p on p.code=rp.permission_code and coalesce(p.reg_status,'A')='A'
  where rp.tenant_id=@tenantId and rp.reg_status='A' and p.code=@permissionCode
)
and (
  (not {alias}.is_confidential and not exists(select 1 from ged.document_acl a where a.document_id={alias}.id))
  or exists (
    select 1 from ged.document_acl a
    where a.document_id={alias}.id and a.can_read and (not @write or a.can_write)
      and (a.user_id=@userId or exists (
        select 1 from ged.user_role ur join ged.app_role r on r.id=ur.role_id
        where ur.user_id=@userId and ur.role_id=a.role_id and r.tenant_id=@tenantId))
  )
)
""";

    private async Task<IReadOnlyList<DocumentAccess>> QueryAllowedAsync(Guid tenantId, Guid userId, IReadOnlyCollection<Guid> documentIds, string action, IReadOnlyDictionary<string, string> attributes, CancellationToken ct)
    {
        if (documentIds.Count == 0) return [];
        var permissionCode = action.Trim().ToUpperInvariant() switch
        {
            "VIEW" or "DOCUMENTS.VIEW" => "Documents.View",
            "EDIT" or "UPDATE" or "MANAGE" => "GED.DOCUMENTS",
            _ => action.Trim()
        };
        string? sector = null;
        if (attributes.TryGetValue("sector", out var s) && !string.IsNullOrWhiteSpace(s)) sector = s.Trim();

        await using var connection = await db.OpenAsync(ct);
        // Single and batch reads use exactly the same live record policy. No caller-supplied
        // classification, tenant-wide role bypass, or cached permission result is trusted.
        return (await connection.QueryAsync<DocumentAccess>(new CommandDefinition($"""
select d.id as "Id", d.is_confidential as "Confidential"
from ged.document d
where d.tenant_id=@tenantId and d.id=any(@ids)
  and {DocumentAccessPredicate("d")}
""", new { tenantId, userId, ids = documentIds.ToArray(), permissionCode, write = permissionCode != "Documents.View", hour = DateTime.UtcNow.Hour }, cancellationToken: ct))).ToList();
    }

    private sealed class DocumentAccess { public Guid Id { get; set; } public bool Confidential { get; set; } }
}
