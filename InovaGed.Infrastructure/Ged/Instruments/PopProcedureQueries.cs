using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Instruments;

namespace InovaGed.Infrastructure.Ged.Instruments;

public sealed class PopProcedureQueries : IPopProcedureQueries
{
    private readonly IDbConnectionFactory _db;

    public PopProcedureQueries(IDbConnectionFactory db) => _db = db;

    public async Task<IReadOnlyList<PopProcedureRow>> ListAsync(Guid tenantId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        const string sql = @"
select
  id as Id,
  code as Code,
  title as Title,
  content_md as ContentMd,
  is_active as IsActive,
  created_at as CreatedAt,
  created_by as CreatedBy,
  updated_at as UpdatedAt,
  updated_by as UpdatedBy
from ged.pop_procedure
where tenant_id=@tenant_id and reg_status='A'
order by code;
";
        var rows = await conn.QueryAsync<PopProcedureRow>(sql, new { tenant_id = tenantId });
        return rows.AsList();
    }

    public async Task<IReadOnlyList<PopProcedureVersionRow>> ListVersionsAsync(Guid tenantId, Guid procedureId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        const string sql = @"
select
  id as Id,
  version_no as VersionNo,
  title as Title,
  published_at as PublishedAt,
  published_by as PublishedBy,
  notes as Notes
from ged.pop_procedure_version
where tenant_id=@tenant_id and procedure_id=@pid and reg_status='A'
order by version_no desc;
";
        var rows = await conn.QueryAsync<PopProcedureVersionRow>(sql, new { tenant_id = tenantId, pid = procedureId });
        return rows.AsList();
    }
}