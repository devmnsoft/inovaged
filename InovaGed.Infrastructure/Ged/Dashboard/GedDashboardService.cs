using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Dashboard;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Data;

namespace InovaGed.Infrastructure.Ged.Dashboard;

public sealed class GedDashboardService : IGedDashboardService
{
    private readonly IDbConnectionFactory _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GedDashboardService> _logger;

    public GedDashboardService(IDbConnectionFactory db, IMemoryCache cache, ILogger<GedDashboardService> logger)
    { _db = db; _cache = cache; _logger = logger; }

    public async Task<GedDashboardVm> GetAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        var key = $"ged-dash-v2:{tenantId}";
        if (_cache.TryGetValue<GedDashboardVm>(key, out var cached)) return cached!;

        var vm = new GedDashboardVm();
        using var conn = await _db.OpenAsync(ct);

        await TryLoad(vm, tenantId, userId, "documents", "ged.document", async () =>
        {
            var row = await conn.QuerySingleAsync(new CommandDefinition(@"
select
  count(*)::int as total_documents,
  count(*) filter (where reg_status='A' and upper(status::text)<>'DELETED')::int as active_documents,
  count(*) filter (where upper(status::text)='DELETED')::int as deleted_documents,
  count(*) filter (where reg_status='A' and classification_id is null)::int as unclassified_documents
from ged.document
where tenant_id=@tenantId", new { tenantId }, cancellationToken: ct));
            vm.TotalDocuments = row.total_documents;
            vm.ActiveDocuments = row.active_documents;
            vm.DeletedDocuments = row.deleted_documents;
            vm.UnclassifiedDocuments = row.unclassified_documents;
            vm.DocumentBySituation = new()
            {
                new() { Label = "Ativos", Value = vm.ActiveDocuments },
                new() { Label = "Excluídos", Value = vm.DeletedDocuments },
                new() { Label = "Sem classificação", Value = vm.UnclassifiedDocuments }
            };
        });

        await TryLoad(vm, tenantId, userId, "ocr", "ged.ocr_job", async () =>
        {
            var status = (await conn.QueryAsync<DashboardMetricSlice>(new CommandDefinition(@"
select upper(status::text) as Label, count(*)::int as Value
from ged.ocr_job
where tenant_id=@tenantId
group by status", new { tenantId }, cancellationToken: ct))).ToList();
            vm.OcrByStatus = status;
            vm.OcrPending = status.FirstOrDefault(x => x.Label == "PENDING")?.Value ?? 0;
            vm.OcrProcessing = status.FirstOrDefault(x => x.Label == "PROCESSING")?.Value ?? 0;
            vm.OcrCompleted = status.FirstOrDefault(x => x.Label == "COMPLETED")?.Value ?? 0;
            vm.OcrError = status.FirstOrDefault(x => x.Label == "ERROR")?.Value ?? 0;
            vm.OcrCancelled = status.FirstOrDefault(x => x.Label == "CANCELLED")?.Value ?? 0;

            var row24h = await conn.QuerySingleAsync(new CommandDefinition(@"
select
  count(*) filter (where upper(status::text)='COMPLETED' and finished_at>=now()-interval '24 hour')::int as completed_24h,
  coalesce(avg(extract(epoch from (finished_at-requested_at))) filter (where upper(status::text)='COMPLETED' and finished_at is not null), 0)::numeric(12,2) as avg_sec
from ged.ocr_job where tenant_id=@tenantId", new { tenantId }, cancellationToken: ct));
            vm.OcrCompleted24h = row24h.completed_24h;
            vm.OcrAverageSeconds = row24h.avg_sec;
        });

        await TryLoad(vm, tenantId, userId, "loans", "ged.loan_request", async () =>
        {
            var loan = (await conn.QueryAsync<DashboardMetricSlice>(new CommandDefinition(@"
select upper(status::text) as Label, count(*)::int as Value
from ged.loan_request
where tenant_id=@tenantId and reg_status='A'
group by status", new { tenantId }, cancellationToken: ct))).ToList();
            vm.LoanByStatus = loan;
            vm.LoanRequested = loan.FirstOrDefault(x => x.Label == "REQUESTED")?.Value ?? 0;
            vm.LoanApproved = loan.FirstOrDefault(x => x.Label == "APPROVED")?.Value ?? 0;
            vm.LoanDelivered = loan.FirstOrDefault(x => x.Label == "DELIVERED")?.Value ?? 0;
            vm.LoanReturned = loan.FirstOrDefault(x => x.Label == "RETURNED")?.Value ?? 0;
            vm.LoanCancelled = loan.FirstOrDefault(x => x.Label == "CANCELLED")?.Value ?? 0;

            vm.RecentLoanRequests = (await conn.QueryAsync<RecentLoanRequestDto>(new CommandDefinition(@"
select requested_at as RequestedAt, coalesce(requester_name,'-') as Requester, coalesce(document_title,'-') as Document, coalesce(status::text,'-') as Status
from ged.vw_loan_report
where tenant_id=@tenantId
order by requested_at desc
limit 10", new { tenantId }, cancellationToken: ct))).ToList();

            vm.OverdueLoans = (await conn.QueryAsync<OverdueLoanDto>(new CommandDefinition(@"
select coalesce(protocol_no::text,'-') as ProtocolNo, coalesce(requester_name,'-') as Borrower, coalesce(document_title,'-') as Document, due_at as DueDate
from ged.vw_loan_overdue
where tenant_id=@tenantId and reg_status='A'
order by due_at asc
limit 10", new { tenantId }, cancellationToken: ct))).ToList();
            vm.LoanOverdue = vm.OverdueLoans.Count;
        });

        if (await TableExistsAsync(conn, "ged", "document_folder_move_history", ct))
        {
            await TryLoad(vm, tenantId, userId, "moves", "ged.document_folder_move_history", async () =>
            {
                var row = await conn.QuerySingleAsync(new CommandDefinition(@"
select
count(*) filter (where moved_at::date=current_date)::int as today,
count(*) filter (where moved_at>=now()-interval '7 day')::int as week
from ged.document_folder_move_history where tenant_id=@tenantId", new { tenantId }, cancellationToken: ct));
                vm.FolderMovesToday = row.today;
                vm.FolderMoves7Days = row.week;
                vm.RecentMoves = (await conn.QueryAsync<RecentDocumentMoveDto>(new CommandDefinition(@"
select h.moved_at as MovedAt, coalesce(d.title,'-') as Document, coalesce(ofd.name,'-') as OriginFolder, coalesce(nfd.name,'-') as DestinationFolder,
coalesce(h.moved_by_name,'-') as MovedBy, coalesce(h.reason,'-') as Justification
from ged.document_folder_move_history h
left join ged.document d on d.tenant_id=h.tenant_id and d.id=h.document_id
left join ged.folder ofd on ofd.tenant_id=h.tenant_id and ofd.id=h.old_folder_id
left join ged.folder nfd on nfd.tenant_id=h.tenant_id and nfd.id=h.new_folder_id
where h.tenant_id=@tenantId order by h.moved_at desc limit 10", new { tenantId }, cancellationToken: ct))).ToList();
            });
        }
        else MarkUnavailable(vm, "Indicador de movimentações indisponível nesta instalação.");

        await LoadSecurityAsync(conn, vm, tenantId, userId, ct);
        await LoadRetentionAsync(conn, vm, tenantId, userId, ct);

        vm.ConfidentialDocuments = 0;
        vm.LockedUsers = vm.LockedUsers;

        _cache.Set(key, vm, TimeSpan.FromSeconds(30));
        return vm;
    }

    private async Task LoadSecurityAsync(IDbConnection conn, GedDashboardVm vm, Guid tenantId, Guid userId, CancellationToken ct)
    {
        if (!await TableExistsAsync(conn, "ged", "security_access_failure_log", ct) && !await TableExistsAsync(conn, "ged", "access_failure", ct))
        { MarkUnavailable(vm, "Indicadores de acesso negado indisponíveis nesta instalação."); return; }

        var source = await TableExistsAsync(conn, "ged", "security_access_failure_log", ct) ? "ged.security_access_failure_log" : "ged.access_failure";
        await TryLoad(vm, tenantId, userId, "security", source, async () =>
        {
            vm.AccessDenied24h = await conn.QuerySingleAsync<int>(new CommandDefinition($"select count(*)::int from {source} where tenant_id=@tenantId and created_at>=now()-interval '24 hour'", new { tenantId }, cancellationToken: ct));
            vm.RecentAccessDenied = (await conn.QueryAsync<RecentAccessDeniedDto>(new CommandDefinition($"select created_at as EventTime, coalesce(user_name,'-') as UserName, coalesce(path,'-') as Path, coalesce(ip_address::text,'-') as IpAddress from {source} where tenant_id=@tenantId order by created_at desc limit 10", new { tenantId }, cancellationToken: ct))).ToList();
        });

        await TryLoad(vm, tenantId, userId, "locked-users", "ged.app_user", async () =>
        {
            vm.LockedUsers = await conn.QuerySingleAsync<int>(new CommandDefinition("select count(*)::int from ged.app_user where tenant_id=@tenantId and reg_status='A' and is_locked=true", new { tenantId }, cancellationToken: ct));
        });

        await TryLoad(vm, tenantId, userId, "audit", "ged.audit_log", async () =>
        {
            vm.RecentAuditEvents = (await conn.QueryAsync<RecentAuditEventDto>(new CommandDefinition(@"
select a.event_time as EventTime, coalesce(a.action,'-') as Action, coalesce(a.entity_name,'-') as EntityName, coalesce(a.summary,'-') as Summary, coalesce(u.name,'-') as UserName
from ged.audit_log a left join ged.app_user u on u.tenant_id=a.tenant_id and u.id=a.user_id
where a.tenant_id=@tenantId
order by a.event_time desc
limit 20", new { tenantId }, cancellationToken: ct))).ToList();
        });
    }

    private async Task LoadRetentionAsync(IDbConnection conn, GedDashboardVm vm, Guid tenantId, Guid userId, CancellationToken ct)
    {
        if (!await TableExistsAsync(conn, "ged", "retention_queue", ct))
        { MarkUnavailable(vm, "Indicador de temporalidade indisponível nesta instalação."); return; }

        await TryLoad(vm, tenantId, userId, "retention", "ged.retention_queue", async () =>
        {
            var row = await conn.QuerySingleAsync(new CommandDefinition(@"
select
count(*) filter (where reg_status='A' and due_at<now())::int as expired,
count(*) filter (where reg_status='A' and due_at>=now() and due_at<now()+interval '30 day')::int as due30
from ged.retention_queue where tenant_id=@tenantId", new { tenantId }, cancellationToken: ct));
            vm.RetentionExpired = row.expired;
            vm.RetentionDue30Days = row.due30;
        });
    }

    private void MarkUnavailable(GedDashboardVm vm, string message)
    { vm.PartialFailure = true; vm.WarningMessages.Add(message); }

    private async Task TryLoad(GedDashboardVm vm, Guid tenantId, Guid userId, string indicator, string table, Func<Task> load)
    {
        try { await load(); }
        catch (Exception ex)
        {
            vm.PartialFailure = true;
            vm.WarningMessages.Add($"Falha ao carregar indicador: {indicator}.");
            _logger.LogWarning(ex, "Dashboard partial failure. Tenant={TenantId} User={UserId} Indicator={Indicator} Table={Table}", tenantId, userId, indicator, table);
        }
    }

    private static async Task<bool> TableExistsAsync(IDbConnection conn, string schema, string table, CancellationToken ct)
    {
        var exists = await conn.QuerySingleAsync<int>(new CommandDefinition("select case when exists (select 1 from information_schema.tables where table_schema=@schema and table_name=@table) then 1 else 0 end", new { schema, table }, cancellationToken: ct));
        return exists == 1;
    }

    private static async Task<bool> ColumnExistsAsync(IDbConnection conn, string schema, string table, string column, CancellationToken ct)
    {
        var exists = await conn.QuerySingleAsync<int>(new CommandDefinition("select case when exists (select 1 from information_schema.columns where table_schema=@schema and table_name=@table and column_name=@column) then 1 else 0 end", new { schema, table, column }, cancellationToken: ct));
        return exists == 1;
    }
}
