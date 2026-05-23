using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Dashboard;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Text;

namespace InovaGed.Infrastructure.Ged.Dashboard;

public sealed class GedDashboardService : IGedDashboardService
{
    private readonly IDbConnectionFactory _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GedDashboardService> _logger;

    public GedDashboardService(IDbConnectionFactory db, IMemoryCache cache, ILogger<GedDashboardService> logger)
    { _db = db; _cache = cache; _logger = logger; }

    public async Task<GedDashboardVm> GetAsync(Guid tenantId, Guid userId, bool forceRefresh, CancellationToken ct)
    {
        var key = $"ged-dash:{tenantId}";
        if (!forceRefresh && _cache.TryGetValue<GedDashboardVm>(key, out var cached)) return cached!;

        var vm = new GedDashboardVm();
        using var conn = await _db.OpenAsync(ct);

        var hasSetores = await TableExistsAsync(conn, "ged", "setores", ct);
        var hasConfidentialColumn = await ResolveConfidentialColumnAsync(conn, ct);
        var hasSolicitacoesArquivoId = await ColumnExistsAsync(conn, "ged", "solicitacoes", "arquivo_id", ct);

        await TryLoad(ct, vm, tenantId, userId, conn, async () =>
        {
            var confidentialSql = hasConfidentialColumn is null
                ? "0"
                : $"(select count(*)::int from ged.document d where d.tenant_id=@tenantId and d.reg_status='A' and coalesce(d.{hasConfidentialColumn}, false)=true)";

            var row = await conn.QuerySingleAsync(new CommandDefinition($@"
select
  (select count(*)::int from ged.document d where d.tenant_id=@tenantId and d.reg_status='A') as TotalDocuments,
  (select count(*)::int from ged.document d where d.tenant_id=@tenantId and d.reg_status='A' and d.classification_id is null) as UnclassifiedDocuments,
  (select count(*)::int from ged.ocr_job j where j.tenant_id=@tenantId and j.status='PENDING'::ged.ocr_status_enum) as OcrPending,
  (select count(*)::int from ged.ocr_job j where j.tenant_id=@tenantId and j.status='PROCESSING'::ged.ocr_status_enum) as OcrProcessing,
  (select count(*)::int from ged.ocr_job j where j.tenant_id=@tenantId and j.status='ERROR'::ged.ocr_status_enum) as OcrFailed,
  (select count(*)::int from ged.ocr_job j where j.tenant_id=@tenantId and j.status='COMPLETED'::ged.ocr_status_enum and j.finished_at>=now()-interval '24 hours') as OcrCompleted24h,
  (select count(*)::int from ged.solicitacoes s where s.tenant_id=@tenantId and s.reg_status='A' and upper(s.status::text)='PENDENTE') as PendingLoanRequests,
  (select count(*)::int from ged.loan_request l where l.tenant_id=@tenantId and l.reg_status='A' and l.returned_at is null and l.due_at < now()) as OverdueLoans,
  (select count(*)::int from ged.document_folder_move_history h where h.tenant_id=@tenantId and h.moved_at>=now()-interval '24 hours') as FolderMoves24h,
  (select count(*)::int from ged.retention_queue q where q.tenant_id=@tenantId and q.reg_status='A' and q.due_at < now()) as RetentionExpired,
  (select count(*)::int from ged.retention_queue q where q.tenant_id=@tenantId and q.reg_status='A' and q.due_at >= now() and q.due_at < now()+interval '30 days') as RetentionDue30Days,
  (select count(*)::int from ged.audit_log a where a.tenant_id=@tenantId and a.event_time>=now()-interval '24 hours' and coalesce(a.is_success,false)=false) as AccessDenied24h,
  {confidentialSql} as ConfidentialDocuments;", new { tenantId }, cancellationToken: ct));

            vm.TotalDocuments = row.totaldocuments;
            vm.UnclassifiedDocuments = row.unclassifieddocuments;
            vm.OcrPending = row.ocrpending;
            vm.OcrProcessing = row.ocrprocessing;
            vm.OcrFailed = row.ocrfailed;
            vm.OcrCompleted24h = row.ocrcompleted24h;
            vm.PendingLoanRequests = row.pendingloanrequests;
            vm.OverdueLoans = row.overdueloans;
            vm.FolderMoves24h = row.foldermoves24h;
            vm.RetentionExpired = row.retentionexpired;
            vm.RetentionDue30Days = row.retentiondue30days;
            vm.AccessDenied24h = row.accessdenied24h;
        });

        await TryLoad(ct, vm, tenantId, userId, conn, async () => vm.OcrByStatus = (await conn.QueryAsync<DashboardMetricSlice>(new CommandDefinition("select status::text as Label, count(*)::int as Value from ged.ocr_job where tenant_id=@tenantId group by status", new { tenantId }, cancellationToken: ct))).ToList());

        await TryLoad(ct, vm, tenantId, userId, conn, async () =>
        {
            var confidentialCountExpr = hasConfidentialColumn is null
                ? "0"
                : $"(select count(*)::int from ged.document d where d.tenant_id=@tenantId and d.reg_status='A' and coalesce(d.{hasConfidentialColumn}, false)=true)";

            if (hasConfidentialColumn is null)
            {
                _logger.LogWarning("GED dashboard: coluna de confidencialidade não encontrada em ged.document. Tenant={Tenant}", tenantId);
            }

            vm.DocumentBySituation = (await conn.QueryAsync<DashboardMetricSlice>(new CommandDefinition($@"
select x.label as Label, x.value as Value from (
 values
  ('Classificados', (select count(*)::int from ged.document d where d.tenant_id=@tenantId and d.reg_status='A' and d.classification_id is not null)),
  ('Sem classificação', (select count(*)::int from ged.document d where d.tenant_id=@tenantId and d.reg_status='A' and d.classification_id is null)),
  ('Sigilosos', {confidentialCountExpr}),
  ('Inativos', (select count(*)::int from ged.document d where d.tenant_id=@tenantId and d.reg_status<>'A'))
) x(label,value)", new { tenantId }, cancellationToken: ct))).ToList();
        });

        await TryLoad(ct, vm, tenantId, userId, conn, async () => vm.LoanByStatus = (await conn.QueryAsync<DashboardMetricSlice>(new CommandDefinition("select coalesce(status::text,'N/A') as Label, count(*)::int as Value from ged.loan_request where tenant_id=@tenantId and reg_status='A' group by status", new { tenantId }, cancellationToken: ct))).ToList());
        await TryLoad(ct, vm, tenantId, userId, conn, async () => vm.RecentOcrErrors = (await conn.QueryAsync<RecentOcrErrorVm>(new CommandDefinition(@"select j.finished_at as Date, d.title as Document, j.document_version_id as VersionId, 1 as Attempts, left(coalesce(j.error_message,''),160) as Error from ged.ocr_job j left join ged.document_version dv on dv.tenant_id=j.tenant_id and dv.id=j.document_version_id left join ged.document d on d.tenant_id=dv.tenant_id and d.id=dv.document_id where j.tenant_id=@tenantId and j.status='ERROR'::ged.ocr_status_enum order by j.finished_at desc nulls last, j.requested_at desc limit 10", new { tenantId }, cancellationToken: ct))).ToList());
        await TryLoad(ct, vm, tenantId, userId, conn, async () => vm.RecentMoves = (await conn.QueryAsync<RecentMoveVm>(new CommandDefinition(@"select h.moved_at as Date, d.title as Document, coalesce(ofd.name,'-') as OldFolder, coalesce(nfd.name,'-') as NewFolder, coalesce(h.moved_by_name,'-') as User, coalesce(h.reason,'-') as Reason from ged.document_folder_move_history h left join ged.document d on d.tenant_id=h.tenant_id and d.id=h.document_id left join ged.folder ofd on ofd.tenant_id=h.tenant_id and ofd.id=h.old_folder_id left join ged.folder nfd on nfd.tenant_id=h.tenant_id and nfd.id=h.new_folder_id where h.tenant_id=@tenantId order by h.moved_at desc limit 10", new { tenantId }, cancellationToken: ct))).ToList());

        await TryLoad(ct, vm, tenantId, userId, conn, async () =>
        {
            var sql = new StringBuilder(@"select s.data_solicitacao as Date, coalesce(u.name, s.usuario_nome,'-') as Requester,");
            sql.Append(hasSetores
                ? " coalesce(setor.name,'-') as Sector,"
                : " null::text as Sector,");

            sql.Append(@" coalesce(d.title,'-') as Document, coalesce(s.descricao,'-') as Description, coalesce(s.status::text,'-') as Status
from ged.solicitacoes s
left join ged.users u on u.tenant_id=s.tenant_id and u.id=s.usuario_id");

            if (hasSetores)
            {
                sql.Append(" left join ged.setores setor on setor.tenant_id=s.tenant_id and setor.id=s.setor_id");
            }
            else
            {
                _logger.LogWarning("GED dashboard: tabela ged.setores não encontrada. Tenant={Tenant}", tenantId);
            }

            if (hasSolicitacoesArquivoId)
            {
                sql.Append(" left join ged.document d on d.tenant_id=s.tenant_id and d.id=s.arquivo_id");
            }
            else
            {
                sql.Append(" left join ged.document d on 1=0");
                _logger.LogWarning("GED dashboard: coluna ged.solicitacoes.arquivo_id não encontrada. Tenant={Tenant}", tenantId);
            }

            sql.Append(@"
where s.tenant_id=@tenantId and s.reg_status='A' and upper(s.status::text)='PENDENTE'
order by s.data_solicitacao desc limit 10");

            vm.RecentLoanRequests = (await conn.QueryAsync<RecentLoanRequestVm>(new CommandDefinition(sql.ToString(), new { tenantId }, cancellationToken: ct))).ToList();
        });

        await TryLoad(ct, vm, tenantId, userId, conn, async () => vm.RecentAuditEvents = (await conn.QueryAsync<RecentAuditEventVm>(new CommandDefinition(@"select a.event_time as Date, coalesce(u.name,'-') as User, coalesce(a.entity_name,'-') as Resource, coalesce(a.summary,'-') as Reason, coalesce(a.ip_address::text,'-') as Ip from ged.audit_log a left join ged.users u on u.tenant_id=a.tenant_id and u.id=a.user_id where a.tenant_id=@tenantId and a.event_time>=now()-interval '24 hours' and coalesce(a.is_success,false)=false order by a.event_time desc limit 10", new { tenantId }, cancellationToken: ct))).ToList());

        _cache.Set(key, vm, TimeSpan.FromSeconds(30));
        return vm;
    }

    private async Task<string?> ResolveConfidentialColumnAsync(IDbConnection conn, CancellationToken ct)
    {
        var candidates = new[] { "is_confidential", "security_level", "confidentiality_level", "secrecy_level", "sigilo", "confidential", "access_level", "classification_level" };
        foreach (var candidate in candidates)
        {
            if (await ColumnExistsAsync(conn, "ged", "document", candidate, ct))
            {
                return candidate;
            }
        }

        return null;
    }

    private static async Task<bool> TableExistsAsync(IDbConnection conn, string schema, string table, CancellationToken ct)
    {
        var exists = await conn.QuerySingleAsync<int>(new CommandDefinition(@"
select case when exists (
    select 1 from information_schema.tables
    where table_schema = @schema and table_name = @table
) then 1 else 0 end", new { schema, table }, cancellationToken: ct));
        return exists == 1;
    }

    private static async Task<bool> ColumnExistsAsync(IDbConnection conn, string schema, string table, string column, CancellationToken ct)
    {
        var exists = await conn.QuerySingleAsync<int>(new CommandDefinition(@"
select case when exists (
    select 1 from information_schema.columns
    where table_schema = @schema and table_name = @table and column_name = @column
) then 1 else 0 end", new { schema, table, column }, cancellationToken: ct));
        return exists == 1;
    }

    private async Task TryLoad(CancellationToken ct, GedDashboardVm vm, Guid tenantId, Guid userId, IDbConnection conn, Func<Task> load)
    { try { await load(); } catch (Exception ex) { vm.HasPartialFailures = true; _logger.LogWarning(ex, "Ged dashboard partial failure. Tenant={Tenant} User={User}", tenantId, userId); } }
}
