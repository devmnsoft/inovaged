using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Common.Paging;
using InovaGed.Application.Reports;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Reports;

public sealed class DispositionReportsQueries : IDispositionReportsQueries
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<DispositionReportsQueries> _logger;

    public DispositionReportsQueries(IDbConnectionFactory db, ILogger<DispositionReportsQueries> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<DispositionKpiVM> GetKpisAsync(Guid tenantId, CancellationToken ct)
    {
        try
        {
            // ✅ NÃO usa reg_status (coluna não existe no seu ged.document)
            // ✅ DisposedLast30D: por enquanto retorna 0 (evita depender de disposed_at)
            const string sql = """
            select
              coalesce(sum(case when d.disposition_status = 'ELIMINATION_READY' then 1 else 0 end),0)::bigint as "EliminationReady",
              coalesce(sum(case when d.disposition_status = 'TRANSFER_READY' then 1 else 0 end),0)::bigint as "TransferReady",
              coalesce(sum(case when d.disposition_status = 'REVIEW_REQUIRED' then 1 else 0 end),0)::bigint as "ReviewRequired",
              0::bigint as "DisposedLast30D"
            from ged.document d
            where d.tenant_id = @tenantId;
            """;

            await using var conn = await _db.OpenAsync(ct);

            var vm = await conn.QuerySingleAsync<DispositionKpiVM>(
                new CommandDefinition(sql, new { tenantId }, cancellationToken: ct));

            return vm;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetKpisAsync failed");
            throw;
        }
    }
    public async Task<IReadOnlyList<DispositionRowVM>> ListDispositionAsync(Guid tenantId, DispositionFilter f, CancellationToken ct)
    {
        var sql = @"
select
  document_id as DocumentId,
  doc_code as DocCode,
  doc_title as DocTitle,
  disposition_status as DispositionStatus,
  disposition_at as DispositionAt,
  disposition_case_id as CaseId,
  class_code as ClassCode,
  class_name as ClassName,
  retention_due_at as RetentionDueAt,
  retention_status as RetentionStatus
from ged.vw_disposition_queue
where tenant_id=@tenantId
";
        var p = new DynamicParameters();
        p.Add("tenantId", tenantId);

        if (!string.IsNullOrWhiteSpace(f.Status))
        {
            sql += " and disposition_status = @status ";
            p.Add("status", f.Status);
        }

        if (f.From is not null)
        {
            sql += " and disposition_at >= @from ";
            p.Add("from", f.From);
        }

        if (f.To is not null)
        {
            sql += " and disposition_at < @to ";
            p.Add("to", f.To);
        }

        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            sql += " and (coalesce(doc_title,'') ilike @q or coalesce(doc_code,'') ilike @q or coalesce(class_name,'') ilike @q or coalesce(class_code,'') ilike @q) ";
            p.Add("q", "%" + f.Q.Trim() + "%");
        }

        sql += " order by disposition_at desc nulls last, doc_title limit 500;";

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            var rows = await conn.QueryAsync<DispositionRowVM>(sql, p);
            return rows.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ListDispositionAsync failed");
            throw;
        }
    }

    public async Task<IReadOnlyList<TermRowVM>> ListTermsAsync(Guid tenantId, DateTimeOffset? from, DateTimeOffset? to, string? status, CancellationToken ct)
    {
        var sql = @"
select
  term_id      as ""TermId"",
  term_no      as ""TermNo"",
  case_id      as ""CaseId"",
  term_type    as ""TermType"",
  status       as ""Status"",
  created_at   as ""CreatedAt"",
  signed_at    as ""SignedAt"",
  executed_at  as ""ExecutedAt""
from ged.vw_retention_terms
where tenant_id=@tenantId
";
        var p = new DynamicParameters();
        p.Add("tenantId", tenantId);

        if (from is not null) { sql += " and created_at >= @from "; p.Add("from", from); }
        if (to is not null) { sql += " and created_at < @to "; p.Add("to", to); }
        if (!string.IsNullOrWhiteSpace(status)) { sql += " and status = @status "; p.Add("status", status); }

        sql += " order by created_at desc limit 500;";

        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<TermRowVM>(sql, p);
        return rows.ToList();
    }

    public async Task<PagedResult<DispositionRowVM>> ListDispositionPagedAsync(
     Guid tenantId, DispositionFilter f, int page, int pageSize, CancellationToken ct)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 10 or > 200 ? 50 : pageSize;

        var where = " where tenant_id=@tenantId ";
        var p = new DynamicParameters();
        p.Add("tenantId", tenantId);

        if (!string.IsNullOrWhiteSpace(f.Status))
        {
            where += " and disposition_status=@status ";
            p.Add("status", f.Status);
        }

        if (f.From is not null) { where += " and disposition_at >= @from "; p.Add("from", f.From); }
        if (f.To is not null) { where += " and disposition_at < @to "; p.Add("to", f.To); }

        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            where += " and (coalesce(doc_title,'') ilike @q or coalesce(doc_code,'') ilike @q or coalesce(class_name,'') ilike @q or coalesce(class_code,'') ilike @q) ";
            p.Add("q", "%" + f.Q.Trim() + "%");
        }

        var sqlCount = "select count(1) from ged.vw_disposition_queue " + where + ";";

        var sqlPage = @"
select
  document_id         as ""DocumentId"",
  doc_code            as ""DocCode"",
  doc_title           as ""DocTitle"",
  disposition_status  as ""DispositionStatus"",
  disposition_at      as ""DispositionAt"",
  disposition_case_id as ""CaseId"",
  class_code          as ""ClassCode"",
  class_name          as ""ClassName"",
  retention_due_at    as ""RetentionDueAt"",
  retention_status    as ""RetentionStatus""
from ged.vw_disposition_queue
" + where + @"
order by disposition_at desc nulls last, doc_title
offset @off limit @lim;
";

        p.Add("off", (page - 1) * pageSize);
        p.Add("lim", pageSize);

        try
        {
            await using var conn = await _db.OpenAsync(ct);

            var total = await conn.ExecuteScalarAsync<long>(sqlCount, p);
            var items = (await conn.QueryAsync<DispositionRowVM>(sqlPage, p)).ToList();

            return new PagedResult<DispositionRowVM>(items, page, pageSize, total);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ListDispositionPagedAsync failed");
            throw;
        }
    }
}