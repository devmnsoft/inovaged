using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Operations;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Operations;

public sealed class OperationsDashboardService : IOperationsDashboardService
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<OperationsDashboardService> _logger;

    public OperationsDashboardService(IDbConnectionFactory db, ILogger<OperationsDashboardService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<OperationsDashboardVm> GetSummaryAsync(Guid tenantId, Guid userId, IReadOnlyCollection<string> roles, OperationsDashboardFilter filter, CancellationToken ct)
    {
        filter = NormalizeFilter(filter);
        await using var conn = await _db.OpenAsync(ct);
        var scope = await BuildScopeAsync(conn, tenantId, userId, roles, filter, ct);

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in new[] { "documentQuality", "unclassified", "withoutOcr", "ocrError", "incomplete", "readyToConsolidate", "documentsToday", "accessedToday", "failures" })
        {
            if (key == "documentQuality" && !await TableExistsAsync(conn, "ged", "document_quality_result", ct))
            {
                _logger.LogWarning("Qualidade Documental indisponível: tabela ged.document_quality_result não existe.");
                counts[key] = 0;
                continue;
            }

            counts[key] = await SafeScalarAsync(conn, CountSql(key, scope), new { TenantId = tenantId, UserId = userId, SectorIds = scope.SectorIds, Sector = scope.Sector, From = filter.From, To = filter.To }, ct);
        }

        var loanSectorExpr = await ResolveLoanSectorSqlExpressionAsync(conn, ct);
        var loanCounts = await SafeQueryAsync<StatusCount>(conn, LoanCountsSql(scope, loanSectorExpr), new { TenantId = tenantId, UserId = userId, Sector = scope.Sector }, ct);
        var protocolCounts = await SafeQueryAsync<StatusCount>(conn, ProtocolCountsSql(scope), new { TenantId = tenantId, UserId = userId, SectorIds = scope.SectorIds }, ct);
        counts["loanPending"] = loanCounts.Where(x => IsOneOf(x.Status, "REQUESTED", "SOLICITADO", "PENDING", "PENDENTE")).Sum(x => x.Total);
        counts["loanOverdue"] = await SafeScalarAsync(conn, LoanOverdueSql(scope, loanSectorExpr), new { TenantId = tenantId, UserId = userId, Sector = scope.Sector }, ct);
        counts["protocolWaiting"] = protocolCounts.Where(x => IsOneOf(x.Status, "NOVO", "ABERTO", "EM_ANALISE", "EM_TRAMITACAO")).Sum(x => x.Total);
        counts["protocolReturned"] = protocolCounts.Where(x => x.Status.Contains("DEVOL", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Total);

        var summary = BuildSummary(counts);
        var actions = BuildActions(summary);
        var critical = (await GetGedQueueAsync(tenantId, userId, roles, new OperationsDashboardFilter { Page = 1, PageSize = 10, OnlyCritical = true }, ct)).Items;

        return new OperationsDashboardVm
        {
            Filter = filter,
            Summary = summary,
            NextActions = actions,
            CriticalItems = critical,
            IsGlobalScope = scope.IsAdmin,
            IsSectorScope = scope.IsAdministradorOphir,
            IsPersonalScope = scope.IsArquivistaOphir,
            ScopeLabel = scope.Label
        };
    }

    public async Task<OperationQueuePageDto> GetGedQueueAsync(Guid tenantId, Guid userId, IReadOnlyCollection<string> roles, OperationsDashboardFilter filter, CancellationToken ct)
    {
        filter = NormalizeFilter(filter);
        await using var conn = await _db.OpenAsync(ct);
        var scope = await BuildScopeAsync(conn, tenantId, userId, roles, filter, ct);
        var queue = string.IsNullOrWhiteSpace(filter.PendingType) ? "unclassified" : filter.PendingType!;
        var sql = GedQueueSql(queue, scope, filter, count: false);
        var countSql = GedQueueSql(queue, scope, filter, count: true);
        var args = new { TenantId = tenantId, UserId = userId, SectorIds = scope.SectorIds, Queue = queue, Offset = (filter.Page - 1) * filter.PageSize, Limit = filter.PageSize };
        var items = await SafeQueryAsync<OperationQueueItemDto>(conn, sql, args, ct);
        var total = await SafeScalarAsync(conn, countSql, args, ct);
        return Page(items, filter, total);
    }

    public async Task<OperationQueuePageDto> GetLoanQueueAsync(Guid tenantId, Guid userId, IReadOnlyCollection<string> roles, OperationsDashboardFilter filter, CancellationToken ct)
    {
        filter = NormalizeFilter(filter);
        await using var conn = await _db.OpenAsync(ct);
        var scope = await BuildScopeAsync(conn, tenantId, userId, roles, filter, ct);
        var sectorExpr = await ResolveLoanSectorSqlExpressionAsync(conn, ct);
        var args = new { TenantId = tenantId, UserId = userId, Sector = scope.Sector, Status = filter.Status, OnlyOverdue = filter.OnlyOverdue, Offset = (filter.Page - 1) * filter.PageSize, Limit = filter.PageSize };
        var items = await SafeQueryAsync<OperationQueueItemDto>(conn, LoanQueueSql(scope, sectorExpr, count: false), args, ct);
        var total = await SafeScalarAsync(conn, LoanQueueSql(scope, sectorExpr, count: true), args, ct);
        return Page(items, filter, total);
    }

    public async Task<OperationQueuePageDto> GetProtocolQueueAsync(Guid tenantId, Guid userId, IReadOnlyCollection<string> roles, OperationsDashboardFilter filter, CancellationToken ct)
    {
        filter = NormalizeFilter(filter);
        await using var conn = await _db.OpenAsync(ct);
        var scope = await BuildScopeAsync(conn, tenantId, userId, roles, filter, ct);
        var args = new { TenantId = tenantId, UserId = userId, SectorIds = scope.SectorIds, Status = filter.Status, Offset = (filter.Page - 1) * filter.PageSize, Limit = filter.PageSize };
        var items = await SafeQueryAsync<OperationQueueItemDto>(conn, ProtocolQueueSql(scope, count: false), args, ct);
        var total = await SafeScalarAsync(conn, ProtocolQueueSql(scope, count: true), args, ct);
        return Page(items, filter, total);
    }

    public async Task<OperationQueuePageDto> GetAlertsAsync(Guid tenantId, Guid userId, IReadOnlyCollection<string> roles, OperationsDashboardFilter filter, CancellationToken ct)
    {
        filter = NormalizeFilter(filter);
        await using var conn = await _db.OpenAsync(ct);
        var scope = await BuildScopeAsync(conn, tenantId, userId, roles, filter, ct);
        var alerts = new List<OperationQueueItemDto>();
        var sectorExpr = await ResolveLoanSectorSqlExpressionAsync(conn, ct);
        alerts.AddRange(await SafeQueryAsync<OperationQueueItemDto>(conn, AlertsSql(scope, sectorExpr), new { TenantId = tenantId, UserId = userId, SectorIds = scope.SectorIds, Sector = scope.Sector, Offset = (filter.Page - 1) * filter.PageSize, Limit = filter.PageSize }, ct));
        var filtered = filter.OnlyCritical ? alerts.Where(x => x.Severity == "critical").ToList() : alerts;
        return Page(filtered.Take(filter.PageSize).ToList(), filter, filtered.Count);
    }

    private static IReadOnlyList<OperationsSummaryDto> BuildSummary(IReadOnlyDictionary<string, int> c) => new[]
    {
        Card("documentQuality", "Qualidade Documental", "Críticos, em atenção, sem OCR e risco LGPD. Se aparecer 0 com schema pendente, execute as migrations.", c, "danger", "/DocumentQuality?Status=Crítico", "Ver pendências"),
        Card("unclassified", "Documentos sem classificação", "Aguardam definição de tipo/classe.", c, "warning", "/GedClassification/Queue", "Classificar agora"),
        Card("withoutOcr", "Documentos sem OCR", "Precisam de texto pesquisável.", c, "secondary", "/Ged/Processing?status=without-ocr", "Executar OCR"),
        Card("ocrError", "OCR com erro", "Falhas de processamento OCR.", c, "danger", "/Ged/Processing?status=error", "Reprocessar OCR"),
        Card("incomplete", "Documentos incompletos", "Aguardam novas partes.", c, "danger", "/Ged?partialStatus=INCOMPLETE", "Ver incompletos"),
        Card("readyToConsolidate", "Prontos para consolidar", "Partes prontas para união.", c, "success", "/Ged?partialStatus=READY_TO_CONSOLIDATE", "Consolidar"),
        Card("loanPending", "Solicitações pendentes", "Pedidos aguardando aprovação.", c, "warning", "/Loans?status=REQUESTED", "Aprovar solicitações"),
        Card("loanOverdue", "Empréstimos vencidos", "Itens com prazo expirado.", c, "danger", "/Loans/Overdue", "Cobrar devolução"),
        Card("protocolWaiting", "Protocolos aguardando análise", "Protocolos novos ou em análise.", c, "info", "/Protocolo?visao=entrada", "Analisar"),
        Card("protocolReturned", "Protocolos devolvidos", "Precisam de ajuste/complementação.", c, "warning", "/Protocolo?status=DEVOLVIDO", "Ajustar protocolo"),
        Card("documentsToday", "Documentos enviados hoje", "Uploads realizados hoje.", c, "primary", "/Ged?period=today", "Ver itens"),
        Card("accessedToday", "Documentos acessados hoje", "Consultas registradas hoje.", c, "primary", "/Audit?entity=DOCUMENT", "Ver acessos"),
        Card("failures", "Falhas recentes de sistema", "Erros e eventos críticos recentes.", c, "dark", "/SystemLogs", "Ver falhas")
    };

    private static OperationsSummaryDto Card(string key, string title, string desc, IReadOnlyDictionary<string, int> c, string css, string url, string action) => new()
    { Key = key, Title = title, Description = desc, Count = c.GetValueOrDefault(key), CssClass = css, Severity = css is "danger" or "dark" ? "critical" : css is "warning" ? "high" : "medium", Url = url, ActionLabel = action };

    private static IReadOnlyList<OperationActionDto> BuildActions(IEnumerable<OperationsSummaryDto> cards) => cards.Where(x => x.Count > 0)
        .Select((x, i) => new OperationActionDto { Key = x.Key, Message = $"{x.Count} {x.Title.ToLowerInvariant()}.", ButtonText = x.ActionLabel, Url = x.Url, Severity = x.Severity, Priority = x.Severity == "critical" ? i : i + 20 })
        .OrderBy(x => x.Priority).Take(7).ToList();

    private static string CountSql(string key, Scope scope) => key switch
    {
        "documentQuality" => "select count(*) from (select distinct on (r.tenant_id,r.document_id) r.quality_status, r.has_ocr, r.has_lgpd_risk from ged.document_quality_result r where r.tenant_id=@TenantId order by r.tenant_id,r.document_id,r.analyzed_at_utc desc) q where q.quality_status in ('Crítico','Atenção') or q.has_ocr=false or q.has_lgpd_risk=true",
        "unclassified" => "select count(*) from ged.document d where d.tenant_id=@TenantId and d.reg_status='A' and (d.type_id is null or d.classification_id is null)" + DocumentScope(scope),
        "withoutOcr" => "select count(*) from ged.document d left join ged.document_version v on v.tenant_id=d.tenant_id and v.id=d.current_version_id where d.tenant_id=@TenantId and d.reg_status='A' and nullif(coalesce(v.ocr_text,''),'') is null" + DocumentScope(scope),
        "ocrError" => "select count(distinct coalesce(v.document_id,d.id)) from ged.ocr_job j left join ged.document_version v on v.id=j.document_version_id left join ged.document d on d.tenant_id=j.tenant_id and d.current_version_id=v.id where j.tenant_id=@TenantId and upper(j.status::text) in ('ERROR','FAILED','FAILURE')" + DocumentScope(scope),
        "incomplete" => "select count(*) from ged.document d join ged.document_version v on v.tenant_id=d.tenant_id and v.id=d.current_version_id where d.tenant_id=@TenantId and d.reg_status='A' and (coalesce(v.is_document_incomplete,false) or upper(coalesce(v.partial_status,''))='INCOMPLETE')" + DocumentScope(scope),
        "readyToConsolidate" => "select count(*) from ged.document d join ged.document_version v on v.tenant_id=d.tenant_id and v.id=d.current_version_id where d.tenant_id=@TenantId and d.reg_status='A' and upper(coalesce(v.partial_status,'')) in ('READY','READY_TO_CONSOLIDATE','READY_FOR_CONSOLIDATION')" + DocumentScope(scope),
        "documentsToday" => "select count(*) from ged.document d where d.tenant_id=@TenantId and d.reg_status='A' and d.created_at >= current_date" + DocumentScope(scope),
        "accessedToday" => "select count(*) from ged.app_audit_log a where a.tenant_id=@TenantId and a.created_at >= current_date and upper(coalesce(a.action,'')) like '%DOCUMENT%' and upper(coalesce(a.action,'')) like '%VIEW%'",
        "failures" => "select count(*) from ged.app_audit_log a where a.tenant_id=@TenantId and a.created_at >= now() - interval '24 hours' and (upper(coalesce(a.action,'')) like '%ERROR%' or upper(coalesce(a.event_type,'')) like '%ERROR%' or coalesce(a.status_code,0) >= 500)",
        _ => "select 0"
    };

    private static string GedQueueSql(string queue, Scope scope, OperationsDashboardFilter filter, bool count)
    {
        var select = count ? "select count(*)" : "select d.id as Id, @Queue as Queue, coalesce(d.title,d.code) as Title, d.code as Code, f.name as Folder, dt.name as DocumentType, coalesce(v.ocr_status,'PENDENTE') as Ocr, v.created_at as UploadedAt, d.status::text as Status, case when @Queue='ocrError' then coalesce(j.error_message,'Falha OCR') end as Error, null::int as Attempts, null::int as Parts, case when @Queue='unclassified' then 'Classificar' when @Queue='withoutOcr' then 'Executar OCR' when @Queue='ocrError' then 'Tentar novamente' when @Queue='incomplete' then 'Adicionar parte' else 'Consolidar' end as ActionLabel, '/Ged/Details/' || d.id as ActionUrl, case when @Queue in ('ocrError','incomplete') then 'critical' else 'high' end as Severity";
        var where = queue switch
        {
            "withoutOcr" => " and nullif(coalesce(v.ocr_text,''),'') is null",
            "ocrError" => " and upper(coalesce(j.status::text,'')) in ('ERROR','FAILED','FAILURE')",
            "incomplete" => " and (coalesce(v.is_document_incomplete,false) or upper(coalesce(v.partial_status,''))='INCOMPLETE')",
            "readyToConsolidate" => " and upper(coalesce(v.partial_status,'')) in ('READY','READY_TO_CONSOLIDATE','READY_FOR_CONSOLIDATION')",
            _ => " and (d.type_id is null or d.classification_id is null)"
        };
        var order = count ? "" : " order by coalesce(v.created_at,d.created_at) desc offset @Offset limit @Limit";
        return $"{select} from ged.document d left join ged.document_version v on v.tenant_id=d.tenant_id and v.id=d.current_version_id left join ged.folder f on f.tenant_id=d.tenant_id and f.id=d.folder_id left join ged.document_type dt on dt.tenant_id=d.tenant_id and dt.id=d.type_id left join ged.ocr_job j on j.tenant_id=d.tenant_id and (j.document_version_id=v.id) where d.tenant_id=@TenantId and d.reg_status='A' {DocumentScope(scope)} {where}{order}";
    }

    private static string LoanCountsSql(Scope scope, string sectorExpr) => "select status::text as Status, count(*)::int as Total from ged.loan_request l where l.tenant_id=@TenantId and l.reg_status='A'" + LoanScope(scope, sectorExpr) + " group by status::text";
    private static string LoanOverdueSql(Scope scope, string sectorExpr) => "select count(*) from ged.loan_request l where l.tenant_id=@TenantId and l.reg_status='A' and l.due_at < now() and upper(l.status::text) in ('APPROVED','DELIVERED','APROVADO','ENTREGUE')" + LoanScope(scope, sectorExpr);
    private static string LoanQueueSql(Scope scope, string sectorExpr, bool count)
    {
        var scopeSql = LoanScope(scope, sectorExpr);
        var select = count ? "select count(*)" : $"select l.id as Id, 'loans' as Queue, 'Solicitação #' || l.protocol_no as Title, l.protocol_no::text as Protocol, l.requester_name as Requester, {sectorExpr} as Sector, l.status::text as Status, l.due_at as DueAt, (select count(*)::int from ged.loan_request_item i where i.tenant_id=l.tenant_id and i.loan_request_id=l.id and coalesce(i.reg_status,'A')='A') as ItemsCount, case when l.due_at < now() then 'Cobrar devolução' when upper(l.status::text) in ('REQUESTED','SOLICITADO') then 'Aprovar' when upper(l.status::text) in ('APPROVED','APROVADO') then 'Entregar' else 'Ver detalhes' end as ActionLabel, '/Loans/' || l.id as ActionUrl, case when l.due_at < now() then 'critical' else 'high' end as Severity";
        var page = count ? "" : " order by l.due_at nulls last, l.requested_at desc offset @Offset limit @Limit";
        return select + " from ged.loan_request l where l.tenant_id=@TenantId and l.reg_status='A' and (@Status is null or @Status='' or upper(l.status::text)=upper(@Status)) and (@OnlyOverdue=false or l.due_at < now())" + scopeSql + page;
    }

    private static string ProtocolCountsSql(Scope scope) => "select p.status as Status, count(*)::int as Total from ged.protocolo p where p.tenant_id=@TenantId and p.reg_status='A'" + ProtocolScope(scope) + " group by p.status";
    private static string ProtocolQueueSql(Scope scope, bool count)
    {
        var select = count ? "select count(*)" : "select p.id as Id, 'protocols' as Queue, coalesce(p.assunto,p.numero) as Title, p.numero as Protocol, p.solicitante_nome as Requester, sa.nome as Sector, p.status as Status, p.updated_at as UpdatedAt, p.tipo_solicitacao as DocumentType, case when p.status in ('NOVO','ABERTO') then 'Assumir' when p.status like '%DEVOL%' then 'Ajustar protocolo' when p.status in ('EM_ANALISE','EM_TRAMITACAO') then 'Analisar' else 'Ver histórico' end as ActionLabel, '/Protocolo/Details/' || p.id as ActionUrl, case when p.status like '%DEVOL%' then 'critical' else 'high' end as Severity";
        var page = count ? "" : " order by coalesce(p.updated_at,p.created_at) desc offset @Offset limit @Limit";
        return select + " from ged.protocolo p left join ged.protocolo_setor sa on sa.tenant_id=p.tenant_id and sa.id=p.setor_atual_id where p.tenant_id=@TenantId and p.reg_status='A' and (@Status is null or @Status='' or p.status=@Status)" + ProtocolScope(scope) + page;
    }

    private static string AlertsSql(Scope scope, string sectorExpr) => $"""
select d.id as Id, 'alerts' as Queue, 'Documento sem classificação há mais de 5 dias' as Title, d.code as Code, d.title as Protocol, null::text as Requester, null::text as Sector, d.created_at as UploadedAt, 'Classificar agora' as ActionLabel, '/Ged/Details/' || d.id as ActionUrl, 'critical' as Severity
from ged.document d where d.tenant_id=@TenantId and d.reg_status='A' and (d.type_id is null or d.classification_id is null) and d.created_at < now() - interval '5 days' {DocumentScope(scope)}
union all
select null::uuid as Id, 'alerts' as Queue, 'OCR com erro repetido' as Title, null::text as Code, coalesce(j.error_message,'Falha OCR') as Protocol, null::text, null::text, j.requested_at, 'Reprocessar OCR', '/Ged/Processing?status=error', 'critical'
from ged.ocr_job j where j.tenant_id=@TenantId and upper(j.status::text) in ('ERROR','FAILED','FAILURE') and j.requested_at < now() - interval '1 hour'
union all
select l.id, 'alerts', 'Empréstimo vencido há mais de 3 dias', l.protocol_no::text, l.requester_name, l.requester_name, {sectorExpr}, l.due_at, 'Cobrar devolução', '/Loans/' || l.id, 'critical'
from ged.loan_request l where l.tenant_id=@TenantId and l.reg_status='A' and l.due_at < now() - interval '3 days' {LoanScope(scope, sectorExpr)}
order by 8 desc nulls last offset @Offset limit @Limit
""";

    private static string DocumentScope(Scope scope) => scope.IsAdmin ? string.Empty : scope.IsArquivistaOphir ? " and d.created_by=@UserId" : string.Empty;
    private static string LoanScope(Scope scope, string sectorExpr) => scope.IsAdmin ? string.Empty : scope.IsAdministradorOphir && sectorExpr != "null::text" ? $" and nullif(coalesce({sectorExpr},''),'') = @Sector" : scope.IsAdministradorOphir ? string.Empty : " and l.requester_id=@UserId";
    private static string ProtocolScope(Scope scope) => scope.IsAdmin ? string.Empty : " and (p.setor_atual_id = any(@SectorIds) or exists(select 1 from ged.protocolo_setor_participante sp where sp.tenant_id=p.tenant_id and sp.protocolo_id=p.id and sp.setor_id=any(@SectorIds) and sp.pode_visualizar=true))";

    private async Task<Scope> BuildScopeAsync(Npgsql.NpgsqlConnection conn, Guid tenantId, Guid userId, IReadOnlyCollection<string> roles, OperationsDashboardFilter filter, CancellationToken ct)
    {
        var normalized = roles.Select(Norm).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scope = new Scope { IsAdmin = normalized.Contains("ADMIN"), IsAdministradorOphir = normalized.Contains("ADMINISTRADOROPHIR"), IsArquivistaOphir = normalized.Contains("ARQUIVISTAOPHIR") };
        scope.Sector = await SafeStringAsync(conn, "select nullif(coalesce(s.setor, s.lotacao, ''), '') from ged.app_user u left join ged.servidor s on s.tenant_id=u.tenant_id and s.id=u.servidor_id where u.tenant_id=@TenantId and u.id=@UserId limit 1", new { TenantId = tenantId, UserId = userId }, ct);
        scope.SectorIds = (await SafeQueryAsync<Guid>(conn, "select us.setor_id from ged.protocolo_usuario_setor us where us.tenant_id=@TenantId and us.usuario_id=@UserId and us.reg_status='A' and us.ativo=true", new { TenantId = tenantId, UserId = userId }, ct)).ToArray();
        scope.Label = scope.IsAdmin ? "Visão global" : scope.IsAdministradorOphir ? $"Setor: {scope.Sector ?? "não vinculado"}" : "Meus itens e solicitações";
        return scope;
    }

    private static OperationsDashboardFilter NormalizeFilter(OperationsDashboardFilter? filter)
    {
        filter ??= new OperationsDashboardFilter();
        filter.Page = Math.Max(1, filter.Page);
        filter.PageSize = Math.Clamp(filter.PageSize <= 0 ? 10 : filter.PageSize, 1, 50);
        return filter;
    }

    private async Task<string> ResolveLoanSectorSqlExpressionAsync(Npgsql.NpgsqlConnection conn, CancellationToken ct)
    {
        foreach (var column in new[] { "requester_sector", "sector_name", "requesting_sector", "requester_department", "department_name" })
        {
            if (await ColumnExistsAsync(conn, "ged", "loan_request", column, ct))
                return $"l.{column}";
        }

        return "null::text";
    }

    private async Task<bool> ColumnExistsAsync(Npgsql.NpgsqlConnection conn, string schema, string table, string column, CancellationToken ct)
    {
        try
        {
            return await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
select exists (
    select 1
    from information_schema.columns
    where table_schema = @schema and table_name = @table and column_name = @column
);
""", new { schema, table, column }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao verificar existência da coluna {Schema}.{Table}.{Column}.", schema, table, column);
            return false;
        }
    }

    private async Task<bool> TableExistsAsync(Npgsql.NpgsqlConnection conn, string schema, string table, CancellationToken ct)
    {
        try
        {
            return await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
select exists (
    select 1
    from information_schema.tables
    where table_schema = @schema and table_name = @table
);
""", new { schema, table }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao verificar existência da tabela {Schema}.{Table}.", schema, table);
            return false;
        }
    }

    private async Task<int> SafeScalarAsync(Npgsql.NpgsqlConnection conn, string sql, object? args, CancellationToken ct)
    {
        try { return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, args, cancellationToken: ct)); }
        catch (Exception ex) { _logger.LogWarning(ex, "Operations scalar failed: {Sql}", sql); return 0; }
    }

    private async Task<string?> SafeStringAsync(Npgsql.NpgsqlConnection conn, string sql, object? args, CancellationToken ct)
    {
        try { return await conn.ExecuteScalarAsync<string?>(new CommandDefinition(sql, args, cancellationToken: ct)); }
        catch { return null; }
    }

    private async Task<IReadOnlyList<T>> SafeQueryAsync<T>(Npgsql.NpgsqlConnection conn, string sql, object? args, CancellationToken ct)
    {
        try { return (await conn.QueryAsync<T>(new CommandDefinition(sql, args, cancellationToken: ct))).ToList(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Operations query failed: {Sql}", sql); return Array.Empty<T>(); }
    }

    private static OperationQueuePageDto Page(IReadOnlyList<OperationQueueItemDto> items, OperationsDashboardFilter filter, int total) => new() { Items = items, Page = filter.Page, PageSize = filter.PageSize, Total = total };
    private static string Norm(string? value) => (value ?? string.Empty).Trim().Replace(" ", "").Replace("_", "").Replace("-", "").ToUpperInvariant();
    private static bool IsOneOf(string status, params string[] values) => values.Any(v => string.Equals(status, v, StringComparison.OrdinalIgnoreCase));

    private sealed class Scope
    {
        public bool IsAdmin { get; set; }
        public bool IsAdministradorOphir { get; set; }
        public bool IsArquivistaOphir { get; set; }
        public string? Sector { get; set; }
        public Guid[] SectorIds { get; set; } = Array.Empty<Guid>();
        public string? Label { get; set; }
    }

    private sealed class StatusCount
    {
        public string Status { get; set; } = string.Empty;
        public int Total { get; set; }
    }
}
