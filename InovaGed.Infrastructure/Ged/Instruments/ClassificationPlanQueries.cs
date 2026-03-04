using System.Text;
using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Instruments;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged.Instruments;

public sealed class ClassificationPlanQueries : IClassificationPlanQueries
{
    private readonly IDbConnectionFactory _db;
    private readonly IAuditWriter _audit;
    private readonly ILogger<ClassificationPlanQueries> _logger;

    public ClassificationPlanQueries(IDbConnectionFactory db, IAuditWriter audit, ILogger<ClassificationPlanQueries> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ClassificationPlanRow>> ListAsync(Guid tenantId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        const string sql = @"
select
  id as Id,
  tenant_id as TenantId,
  code as Code,
  name as Name,
  description as Description,
  parent_id as ParentId,
  retention_start_event::text as RetentionStartEvent,
  retention_active_days as RetentionActiveDays,
  retention_active_months as RetentionActiveMonths,
  retention_active_years as RetentionActiveYears,
  retention_archive_days as RetentionArchiveDays,
  retention_archive_months as RetentionArchiveMonths,
  retention_archive_years as RetentionArchiveYears,
  final_destination::text as FinalDestination,
  requires_digital_signature as RequiresDigitalSignature,
  is_confidential as IsConfidential,
  is_active as IsActive,
  retention_notes as RetentionNotes
from ged.classification_plan
where tenant_id=@tenant_id
order by code;
";
        var rows = await conn.QueryAsync<ClassificationPlanRow>(new CommandDefinition(sql, new { tenant_id = tenantId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<ClassificationPlanVersionRow>> ListVersionsAsync(Guid tenantId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        const string sql = @"
select
  id as Id,
  version_no as VersionNo,
  title as Title,
  notes as Notes,
  published_at as PublishedAt,
  published_by as PublishedBy
from ged.classification_plan_version
where tenant_id=@tenant_id
order by version_no desc;
";
        var rows = await conn.QueryAsync<ClassificationPlanVersionRow>(new CommandDefinition(sql, new { tenant_id = tenantId }, cancellationToken: ct));
        return rows.AsList();
    }

    // ✅ Item 3: export por classe/inteiro (CSV)
    public async Task<string> ExportCurrentCsvAsync(Guid tenantId, Guid? rootId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);

        const string sqlAll = @"
select
  cp.code,
  cp.name,
  cp.description,
  p.code as parent_code,
  cp.retention_start_event::text as retention_start_event,
  cp.retention_active_days, cp.retention_active_months, cp.retention_active_years,
  cp.retention_archive_days, cp.retention_archive_months, cp.retention_archive_years,
  cp.final_destination::text as final_destination,
  cp.requires_digital_signature,
  cp.is_confidential,
  cp.is_active,
  cp.retention_notes
from ged.classification_plan cp
left join ged.classification_plan p on p.tenant_id=cp.tenant_id and p.id=cp.parent_id
where cp.tenant_id=@tenant_id
order by cp.code;
";

        const string sqlRoot = @"
with recursive tree as (
  select id
  from ged.classification_plan
  where tenant_id=@tenant_id and id=@root_id
  union all
  select c.id
  from ged.classification_plan c
  join tree t on c.parent_id=t.id
  where c.tenant_id=@tenant_id
)
select
  cp.code,
  cp.name,
  cp.description,
  p.code as parent_code,
  cp.retention_start_event::text as retention_start_event,
  cp.retention_active_days, cp.retention_active_months, cp.retention_active_years,
  cp.retention_archive_days, cp.retention_archive_months, cp.retention_archive_years,
  cp.final_destination::text as final_destination,
  cp.requires_digital_signature,
  cp.is_confidential,
  cp.is_active,
  cp.retention_notes
from ged.classification_plan cp
join tree t on t.id=cp.id
left join ged.classification_plan p on p.tenant_id=cp.tenant_id and p.id=cp.parent_id
where cp.tenant_id=@tenant_id
order by cp.code;
";

        var data = rootId.HasValue
            ? await conn.QueryAsync(new CommandDefinition(sqlRoot, new { tenant_id = tenantId, root_id = rootId }, cancellationToken: ct))
            : await conn.QueryAsync(new CommandDefinition(sqlAll, new { tenant_id = tenantId }, cancellationToken: ct));

        var csv = ToCsv(data);

        _ = await _audit.WriteAsync(tenantId, null, "REPORT_PRINT", "classification_plan", rootId,
            "Exportação CSV do PCD/TTD", null, null, new { rootId }, ct);

        return csv;
    }

    public async Task<string> ExportVersionCsvAsync(Guid tenantId, Guid versionId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);

        const string sql = @"
select
  code,
  name,
  description,
  parent_code,
  retention_start_event::text as retention_start_event,
  retention_active_days, retention_active_months, retention_active_years,
  retention_archive_days, retention_archive_months, retention_archive_years,
  final_destination::text as final_destination,
  requires_digital_signature,
  is_confidential,
  is_active,
  retention_notes
from ged.classification_plan_version_item
where tenant_id=@tenant_id and version_id=@version_id
order by code;
";
        var data = await conn.QueryAsync(new CommandDefinition(sql, new { tenant_id = tenantId, version_id = versionId }, cancellationToken: ct));
        var csv = ToCsv(data);

        _ = await _audit.WriteAsync(tenantId, null, "REPORT_PRINT", "classification_plan_version", versionId,
            "Exportação CSV da versão do PCD/TTD", null, null, new { versionId }, ct);

        return csv;
    }

    private static string ToCsv(IEnumerable<dynamic> rows)
    {
        // simples e suficiente para PoC
        var list = rows.ToList();
        if (list.Count == 0) return "";

        var dict0 = (IDictionary<string, object?>)list[0];
        var headers = dict0.Keys.ToList();

        static string Esc(object? v)
        {
            var s = v?.ToString() ?? "";
            if (s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
                s = "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(Esc)));

        foreach (var r in list)
        {
            var d = (IDictionary<string, object?>)r;
            sb.AppendLine(string.Join(",", headers.Select(h => Esc(d.TryGetValue(h, out var v) ? v : null))));
        }
        return sb.ToString();
    }
}