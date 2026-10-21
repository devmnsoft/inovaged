using System.Data.Common;
using Dapper;

namespace InovaGed.Infrastructure.Security;

/// <summary>Reads the identity and legacy role models without assuming that their IDs match.</summary>
internal static class UserRoleReader
{
    public static async Task<HashSet<string>> ReadAsync(DbConnection connection, DbTransaction? transaction,
        Guid tenantId, Guid userId, CancellationToken ct)
        => (await AssignmentsAsync(connection, transaction, tenantId, userId, ct)).Select(x => Normalize(x.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static async Task<Guid[]> RoleIdsAsync(DbConnection connection, DbTransaction? transaction,
        Guid tenantId, Guid userId, CancellationToken ct)
        => (await AssignmentsAsync(connection, transaction, tenantId, userId, ct)).Select(x => x.Id).Distinct().ToArray();

    private static async Task<IReadOnlyList<Assignment>> AssignmentsAsync(DbConnection connection, DbTransaction? transaction,
        Guid tenantId, Guid userId, CancellationToken ct)
    {
        var columns = (await connection.QueryAsync<Column>(new CommandDefinition("""
select table_name as "Table", column_name as "Name"
from information_schema.columns where table_schema='ged'
and table_name in ('app_user_role','user_role','user_roles','app_role','role','roles')
""", transaction: transaction, cancellationToken: ct))).ToList();
        bool Has(string table, string column) => columns.Any(c => c.Table == table && c.Name == column);
        var queries = new List<string>();
        foreach (var link in new[] { "app_user_role", "user_role", "user_roles" })
        foreach (var role in link == "user_roles" ? new[] { "roles", "role" } : new[] { "app_role" })
        {
            if (!Has(link, "user_id") || !Has(link, "role_id") || !Has(role, "tenant_id")) continue;
            var name = Has(role, "normalized_name") ? "r.normalized_name" : Has(role, "code") ? "r.code" : "r.name";
            var tenant = Has(link, "tenant_id") ? " and ur.tenant_id=@tenantId" : "";
            var active = Has(role, "reg_status") ? " and r.reg_status='A'" : "";
            // Identifiers come exclusively from the fixed table/column lists above.
            var activeLink = Has(link, "reg_status") ? " and ur.reg_status='A'" : "";
            queries.Add($"select r.id as \"Id\",{name} as \"Name\" from ged.{link} ur join ged.{role} r on r.id=ur.role_id where r.tenant_id=@tenantId and ur.user_id=@userId{tenant}{active}{activeLink}");
        }
        if (queries.Count == 0) return [];
        var roles = await connection.QueryAsync<Assignment>(new CommandDefinition(string.Join(" union ", queries),
            new { tenantId, userId }, transaction, cancellationToken: ct));
        return roles.AsList();
    }

    internal static string Normalize(string value) => value.Replace(" ", "").Replace("_", "").Replace("-", "").ToUpperInvariant();
    private sealed class Column { public string Table { get; set; } = ""; public string Name { get; set; } = ""; }
    private sealed class Assignment { public Guid Id { get; set; } public string Name { get; set; } = ""; }
}
