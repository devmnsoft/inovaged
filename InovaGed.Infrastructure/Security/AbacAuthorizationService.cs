using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Security;

namespace InovaGed.Infrastructure.Security;

public sealed class AbacAuthorizationService : IAbacAuthorizationService
{
    private readonly IDbConnectionFactory _db;
    public AbacAuthorizationService(IDbConnectionFactory db) => _db = db;

    public async Task<bool> CanAccessDocumentAsync(Guid tenantId, Guid userId, Guid documentId, string action, IReadOnlyDictionary<string, string> attributes, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        // Regra ABAC base: classificação + setor + horário comercial.
        var classification = attributes.TryGetValue("classification", out var c) ? c : "PUBLIC";
        var sector = attributes.TryGetValue("sector", out var s) ? s : string.Empty;
        var hour = DateTime.UtcNow.Hour;

        var hasDirectPermission = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(@"
select exists(
    select 1
    from ged.permissions p
    where p.tenant_id=@tenantId and p.user_id=@userId and p.action=@action
)", new { tenantId, userId, action }, cancellationToken: ct));

        if (!hasDirectPermission) return false;
        if (classification.Equals("SENSITIVE", StringComparison.OrdinalIgnoreCase) && (hour < 6 || hour > 20)) return false;
        if (!string.IsNullOrWhiteSpace(sector) && !classification.Equals("PUBLIC", StringComparison.OrdinalIgnoreCase))
        {
            var sameSector = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(@"
select exists(select 1 from ged.documents d join ged.users u on u.tenant_id=d.tenant_id and u.id=@userId where d.tenant_id=@tenantId and d.id=@documentId and d.setor = @sector)",
                new { tenantId, userId, documentId, sector }, cancellationToken: ct));
            return sameSector;
        }

        return true;
    }
}
