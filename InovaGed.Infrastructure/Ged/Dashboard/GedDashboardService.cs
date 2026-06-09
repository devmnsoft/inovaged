using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Dashboard;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Data;

namespace InovaGed.Infrastructure.Ged.Dashboard;

public sealed class GedDashboardService : IGedDashboardService
{
    private static readonly string[] SecurityDateColumnCandidates = ["created_at", "created_at_utc", "reg_date", "occurred_at", "timestamp", "event_at", "event_time", "access_time", "failure_time", "logged_at"];
    private static readonly string[] AuditDateColumnCandidates = ["created_at", "created_at_utc", "reg_date", "occurred_at", "timestamp", "event_at", "event_time"];

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
            vm.DocumentBySituation =
            [
                new() { Label = "Ativos", Value = vm.ActiveDocuments },
                new() { Label = "Excluídos", Value = vm.DeletedDocuments },
                new() { Label = "Sem classificação", Value = vm.UnclassifiedDocuments }
            ];
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

        await LoadLoansAsync(conn, vm, tenantId, userId, ct);

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

        _cache.Set(key, vm, TimeSpan.FromSeconds(30));
        return vm;
    }

    private async Task LoadLoansAsync(IDbConnection conn, GedDashboardVm vm, Guid tenantId, Guid userId, CancellationToken ct)
    {
        if (!await TableExistsAsync(conn, "ged", "loan_request", ct))
        {
            MarkUnavailable(vm, "Indicador de empréstimos parcialmente indisponível.");
            _logger.LogWarning("Dashboard partial failure. Tenant={TenantId} User={UserId} Indicator=loans Table=ged.loan_request Reason=table-not-found", tenantId, userId);
            return;
        }

        await TryLoad(vm, tenantId, userId, "loans", "ged.loan_request", async () =>
        {
            var row = await conn.QuerySingleAsync(new CommandDefinition(@"
select
  count(*) filter (where reg_status='A' and upper(status::text)='REQUESTED')::int as requested,
  count(*) filter (where reg_status='A' and upper(status::text)='APPROVED')::int as approved,
  count(*) filter (where reg_status='A' and upper(status::text)='DELIVERED')::int as delivered,
  count(*) filter (where reg_status='A' and upper(status::text)='RETURNED')::int as returned,
  count(*) filter (where reg_status='A' and upper(status::text)='OVERDUE')::int as overdue,
  count(*) filter (where reg_status='A' and upper(status::text)='CANCELLED')::int as cancelled
from ged.loan_request
where tenant_id=@tenantId", new { tenantId }, cancellationToken: ct));

            vm.LoanRequested = row.requested;
            vm.LoanApproved = row.approved;
            vm.LoanDelivered = row.delivered;
            vm.LoanReturned = row.returned;
            vm.LoanOverdue = row.overdue;
            vm.LoanCancelled = row.cancelled;
            vm.LoanByStatus =
            [
                new() { Label = "REQUESTED", Value = vm.LoanRequested },
                new() { Label = "APPROVED", Value = vm.LoanApproved },
                new() { Label = "DELIVERED", Value = vm.LoanDelivered },
                new() { Label = "RETURNED", Value = vm.LoanReturned },
                new() { Label = "OVERDUE", Value = vm.LoanOverdue },
                new() { Label = "CANCELLED", Value = vm.LoanCancelled }
            ];
        });

        if (await TableExistsAsync(conn, "ged", "vw_loan_report", ct))
        {
            await TryLoad(vm, tenantId, userId, "loans-recent", "ged.vw_loan_report", async () =>
            {
                vm.RecentLoanRequests = (await conn.QueryAsync<RecentLoanRequestDto>(new CommandDefinition(@"
select v.requested_at as RequestedAt,
       coalesce(v.requester_name,'-') as Requester,
       coalesce(v.document_title,'Documento sem título') as Document,
       coalesce(v.status::text,'-') as Status
from ged.vw_loan_report v
where v.tenant_id=@tenantId
order by v.requested_at desc
limit 10", new { tenantId }, cancellationToken: ct))).ToList();
            });
        }
        else
        {
            MarkUnavailable(vm, "Indicador de empréstimos parcialmente indisponível.");
            _logger.LogWarning("Dashboard partial failure. Tenant={TenantId} User={UserId} Indicator=loans-recent Table=ged.vw_loan_report Reason=view-not-found", tenantId, userId);
            vm.RecentLoanRequests = [];
        }

        if (await TableExistsAsync(conn, "ged", "vw_loan_overdue", ct))
        {
            var overdueHasOwnDocumentTitle = await ColumnExistsAsync(conn, "ged", "vw_loan_overdue", "document_title", ct);
            var reportExists = await TableExistsAsync(conn, "ged", "vw_loan_report", ct);
            var reportHasDocumentTitle = reportExists && await ColumnExistsAsync(conn, "ged", "vw_loan_report", "document_title", ct);

            var overdueSql = overdueHasOwnDocumentTitle
                ? @"
select
  coalesce(o.protocol_no::text,'-') as ProtocolNo,
  coalesce(o.requester_name,'-') as Borrower,
  coalesce(o.document_title,'Documento sem título') as Document,
  o.due_at as DueDate
from ged.vw_loan_overdue o
where o.tenant_id=@tenantId and o.reg_status='A'
order by o.due_at asc nulls last
limit 10"
                : reportHasDocumentTitle
                    ? @"
select
  coalesce(o.protocol_no::text,'-') as ProtocolNo,
  coalesce(o.requester_name,'-') as Borrower,
  coalesce(r.document_title,'Documento sem título') as Document,
  o.due_at as DueDate
from ged.vw_loan_overdue o
left join ged.vw_loan_report r
  on r.tenant_id = o.tenant_id
 and r.protocol_no = o.protocol_no
 and r.document_id = o.document_id
where o.tenant_id=@tenantId and o.reg_status='A'
order by o.due_at asc nulls last
limit 10"
                    : @"
select
  coalesce(o.protocol_no::text,'-') as ProtocolNo,
  coalesce(o.requester_name,'-') as Borrower,
  'Documento sem título'::text as Document,
  o.due_at as DueDate
from ged.vw_loan_overdue o
where o.tenant_id=@tenantId and o.reg_status='A'
order by o.due_at asc nulls last
limit 10";

            await TryLoad(vm, tenantId, userId, "loans-overdue", "ged.vw_loan_overdue", async () =>
            {
                vm.OverdueLoans = (await conn.QueryAsync<OverdueLoanDto>(new CommandDefinition(overdueSql, new { tenantId }, cancellationToken: ct))).ToList();

                if (vm.LoanOverdue == 0)
                {
                    vm.LoanOverdue = vm.OverdueLoans.Count;
                }
            });
        }
        else
        {
            vm.OverdueLoans = [];
            vm.LoanOverdue = 0;
        }
    }

    private async Task LoadSecurityAsync(IDbConnection conn, GedDashboardVm vm, Guid tenantId, Guid userId, CancellationToken ct)
    {
        var securitySource = await ResolveSecuritySourceAsync(conn, ct);

        if (securitySource is null)
        {
            vm.AccessDenied24h = 0;
            vm.RecentAccessDenied = [];
            MarkUnavailable(vm, "Nenhum dado encontrado para este indicador.");
            _logger.LogInformation("Dashboard indicator without data source. Tenant={TenantId} User={UserId} Indicator=security Reason=source-or-date-column-not-found", tenantId, userId);
        }
        else
        {
            await TryLoad(vm, tenantId, userId, "security", securitySource.Source, async () =>
            {
                vm.AccessDenied24h = await conn.QuerySingleAsync<int>(new CommandDefinition($"select count(*)::int from {securitySource.Source} where tenant_id=@tenantId and {securitySource.DateColumn}>=now()-interval '24 hour'{securitySource.FilterSql}", new { tenantId }, cancellationToken: ct));
                vm.RecentAccessDenied = (await conn.QueryAsync<RecentAccessDeniedDto>(new CommandDefinition($"select {securitySource.DateColumn} as EventTime, {securitySource.UserNameExpression} as UserName, {securitySource.PathExpression} as Path, {securitySource.IpAddressExpression} as IpAddress from {securitySource.Source} where tenant_id=@tenantId{securitySource.FilterSql} order by {securitySource.DateColumn} desc limit 10", new { tenantId }, cancellationToken: ct))).ToList();
            });
        }

        await TryLoad(vm, tenantId, userId, "locked-users", "ged.app_user", async () =>
        {
            var hasDeletedAt = await ColumnExistsAsync(conn, "ged", "app_user", "deleted_at_utc", ct);
            var sql = hasDeletedAt
                ? "select count(*)::int from ged.app_user u where u.tenant_id=@tenantId and coalesce(u.is_locked,false)=true and u.deleted_at_utc is null"
                : "select count(*)::int from ged.app_user u where u.tenant_id=@tenantId and coalesce(u.is_locked,false)=true";
            vm.LockedUsers = await conn.QuerySingleAsync<int>(new CommandDefinition(sql, new { tenantId }, cancellationToken: ct));
        });

        var auditDateColumn = await ResolveDateColumnAsync(conn, "ged", "audit_log", ct, AuditDateColumnCandidates);
        if (auditDateColumn is null)
        {
            vm.RecentAuditEvents = [];
            MarkUnavailable(vm, "Indicador de auditoria parcialmente indisponível.");
            _logger.LogWarning("Dashboard partial failure. Tenant={TenantId} User={UserId} Indicator=audit Reason=date-column-not-found", tenantId, userId);
            return;
        }

        await TryLoad(vm, tenantId, userId, "audit", "ged.audit_log", async () =>
        {
            vm.RecentAuditEvents = (await conn.QueryAsync<RecentAuditEventDto>(new CommandDefinition($@"
select a.{auditDateColumn} as EventTime, coalesce(a.action::text,'-') as Action, coalesce(a.entity_name,'-') as EntityName, coalesce(a.summary,'-') as Summary, coalesce(u.name,'-') as UserName
from ged.audit_log a left join ged.app_user u on u.tenant_id=a.tenant_id and u.id=a.user_id
where a.tenant_id=@tenantId
order by a.{auditDateColumn} desc
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
        catch (Npgsql.PostgresException ex) when (ex.SqlState is "42703" or "42P01")
        {
            vm.PartialFailure = true;
            var message = indicator == "loans-overdue"
                ? "Indicador de empréstimos vencidos parcialmente indisponível."
                : $"Falha parcial ao carregar indicador: {indicator}.";
            vm.WarningMessages.Add(message);
            _logger.LogWarning(ex, "Dashboard partial failure. Tenant={TenantId} User={UserId} Indicator={Indicator} Table={Table} SqlState={SqlState}", tenantId, userId, indicator, table, ex.SqlState);
        }
        catch (Exception ex)
        {
            vm.PartialFailure = true;
            vm.WarningMessages.Add($"Falha ao carregar indicador: {indicator}.");
            _logger.LogError(ex, "Dashboard unexpected failure. Tenant={TenantId} User={UserId} Indicator={Indicator} Table={Table}", tenantId, userId, indicator, table);
        }
    }

    private static async Task<bool> TableExistsAsync(IDbConnection conn, string schema, string table, CancellationToken ct)
        => await conn.QuerySingleAsync<bool>(new CommandDefinition(@"
select exists (
    select 1
    from information_schema.tables
    where table_schema = @Schema
      and table_name = @Table
)", new { Schema = schema, Table = table }, cancellationToken: ct));

    private static async Task<bool> ColumnExistsAsync(IDbConnection conn, string schema, string table, string column, CancellationToken ct)
        => await conn.QuerySingleAsync<bool>(new CommandDefinition(@"
select exists (
    select 1
    from information_schema.columns
    where table_schema = @Schema
      and table_name = @Table
      and column_name = @Column
)", new { Schema = schema, Table = table, Column = column }, cancellationToken: ct));

    private sealed record SecurityDashboardSource(
        string Source,
        string DateColumn,
        string FilterSql,
        string UserNameExpression,
        string PathExpression,
        string IpAddressExpression);

    private static async Task<SecurityDashboardSource?> ResolveSecuritySourceAsync(IDbConnection conn, CancellationToken ct)
    {
        if (await TableExistsAsync(conn, "ged", "app_audit_log", ct))
        {
            var filter = await BuildAuditSecurityFilterAsync(conn, "ged", "app_audit_log", ct);
            var source = await BuildSecuritySourceAsync(conn, "ged", "app_audit_log", filter, ct, "created_at");
            if (source is not null) return source;
        }

        if (await TableExistsAsync(conn, "ged", "audit_log", ct))
        {
            var filter = await BuildAuditSecurityFilterAsync(conn, "ged", "audit_log", ct);
            var source = await BuildSecuritySourceAsync(conn, "ged", "audit_log", filter, ct, "created_at");
            if (source is not null) return source;

            source = await BuildSecuritySourceAsync(conn, "ged", "audit_log", filter, ct, "reg_date");
            if (source is not null) return source;
        }

        return await BuildSecuritySourceAsync(conn, "ged", "security_access_failure_log", string.Empty, ct)
            ?? await BuildSecuritySourceAsync(conn, "ged", "access_failure", string.Empty, ct)
            ?? await BuildSecuritySourceAsync(conn, "ged", "document", " and false", ct);
    }

    private static async Task<SecurityDashboardSource?> BuildSecuritySourceAsync(IDbConnection conn, string schema, string table, string filterSql, CancellationToken ct, params string[] preferredColumns)
    {
        if (!await TableExistsAsync(conn, schema, table, ct)) return null;

        var candidates = preferredColumns.Length > 0 ? preferredColumns : SecurityDateColumnCandidates;
        var dateColumn = await ResolveDateColumnAsync(conn, schema, table, ct, candidates);
        if (dateColumn is null) return null;

        var userNameExpression = await TextExpressionAsync(conn, schema, table, ["user_name", "user_id", "username", "email"], ct);
        var pathExpression = await TextExpressionAsync(conn, schema, table, ["path", "request_path", "url", "route", "entity_name"], ct);
        var ipAddressExpression = await TextExpressionAsync(conn, schema, table, ["ip_address", "ip", "remote_ip"], ct);
        return new SecurityDashboardSource($"{schema}.{table}", dateColumn, filterSql, userNameExpression, pathExpression, ipAddressExpression);
    }

    private static async Task<string> BuildAuditSecurityFilterAsync(IDbConnection conn, string schema, string table, CancellationToken ct)
    {
        var conditions = new List<string>();

        if (await ColumnExistsAsync(conn, schema, table, "status_code", ct))
        {
            conditions.Add($"coalesce({QuoteIdentifier("status_code")},0) in (401,403)");
        }

        foreach (var column in new[] { "action", "event_type", "message", "summary" })
        {
            if (await ColumnExistsAsync(conn, schema, table, column, ct))
            {
                var columnSql = QuoteIdentifier(column);
                conditions.Add($"upper(coalesce({columnSql}::text,'')) in ('ACCESS_DENIED','ACCESSDENIED','FORBIDDEN','AUTHORIZATION_FAILED','UNAUTHORIZED')");
                conditions.Add($"coalesce({columnSql}::text,'') ilike '%acesso negado%'");
                conditions.Add($"coalesce({columnSql}::text,'') ilike '%access denied%'");
            }
        }

        return conditions.Count == 0 ? " and false" : $" and ({string.Join(" or ", conditions)})";
    }

    private static async Task<string> TextExpressionAsync(IDbConnection conn, string schema, string table, IReadOnlyList<string> candidates, CancellationToken ct)
    {
        var column = await ResolveFirstExistingColumnAsync(conn, schema, table, candidates, ct);
        return column is null ? "'-'::text" : $"coalesce({column}::text,'-')";
    }

    private static async Task<string?> ResolveDateColumnAsync(
        IDbConnection conn,
        string schema,
        string table,
        CancellationToken ct,
        params string[] preferredColumns)
    {
        if (!await TableExistsAsync(conn, schema, table, ct)) return null;
        var candidates = preferredColumns.Length > 0 ? preferredColumns : SecurityDateColumnCandidates;
        return await ResolveFirstExistingColumnAsync(conn, schema, table, candidates, ct);
    }

    private static async Task<string?> ResolveFirstExistingColumnAsync(IDbConnection conn, string schema, string table, IReadOnlyList<string> candidates, CancellationToken ct)
    {
        foreach (var candidate in candidates)
        {
            if (await ColumnExistsAsync(conn, schema, table, candidate, ct))
            {
                return QuoteIdentifier(candidate);
            }
        }

        return null;
    }

    private static string QuoteIdentifier(string identifier)
        => $"\"{identifier.Replace("\"", "\"\"")}\"";
}
