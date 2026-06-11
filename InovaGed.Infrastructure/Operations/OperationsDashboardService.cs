using System.Collections.Concurrent;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Operations;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace InovaGed.Infrastructure.Operations;

public sealed class OperationsDashboardService : IOperationsDashboardService
{
    private static readonly ConcurrentDictionary<string, bool> Warned = new(StringComparer.OrdinalIgnoreCase);
    private readonly IDbConnectionFactory _db;
    private readonly ITableSchemaGuard _schema;
    private readonly ILogger<OperationsDashboardService> _logger;

    public OperationsDashboardService(IDbConnectionFactory db, ITableSchemaGuard schema, ILogger<OperationsDashboardService> logger)
    {
        _db = db;
        _schema = schema;
        _logger = logger;
    }

    public async Task<OperationsDashboardVm> GetSummaryAsync(Guid tenantId, Guid userId, IReadOnlyCollection<string> roles, OperationsDashboardFilter filter, CancellationToken ct)
    {
        filter = NormalizeFilter(filter);
        await using var conn = await _db.OpenAsync(ct);
        var scope = await BuildScopeAsync(conn, tenantId, userId, roles, ct);
        var modules = new[] { "GED", "OCR", "Documentos Parciais", "Loans", "Protocolo", "Qualidade", "Alertas" };
        var statuses = new List<ModuleSchemaStatus>();
        foreach (var m in modules) statuses.Add(await ModuleStatusAsync(m, ct));

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["withoutOcr"] = 0,
            ["ocrError"] = 0,
            ["unclassified"] = 0,
            ["incomplete"] = 0,
            ["readyToConsolidate"] = 0,
            ["protocolWaiting"] = 0,
            ["loanPending"] = 0,
            ["loanOverdue"] = 0,
            ["documentQuality"] = 0,
            ["criticalAlerts"] = 0
        };

        if (IsReady(statuses, "GED"))
        {
            counts["unclassified"] = await SafeScalarAsync(conn, await UnclassifiedCountSqlAsync(conn, scope, ct), Args(tenantId, userId, scope), ct, "GED");
            counts["withoutOcr"] = await SafeScalarAsync(conn, await WithoutOcrCountSqlAsync(conn, scope, ct), Args(tenantId, userId, scope), ct, "OCR");
        }

        if (IsReady(statuses, "OCR"))
            counts["ocrError"] = await SafeScalarAsync(conn, OcrErrorCountSql(scope), Args(tenantId, userId, scope), ct, "OCR");

        if (IsReady(statuses, "Documentos Parciais"))
        {
            counts["incomplete"] = await SafeScalarAsync(conn, PartialCountSql(scope, ready: false), Args(tenantId, userId, scope), ct, "Documentos Parciais");
            counts["readyToConsolidate"] = await SafeScalarAsync(conn, PartialCountSql(scope, ready: true), Args(tenantId, userId, scope), ct, "Documentos Parciais");
        }

        if (IsReady(statuses, "Loans"))
        {
            var loanSectorExpr = await ResolveLoanSectorSqlExpressionAsync(conn, ct);
            counts["loanPending"] = await SafeScalarAsync(conn, LoanPendingCountSql(scope, loanSectorExpr), Args(tenantId, userId, scope), ct, "Loans");
            counts["loanOverdue"] = await SafeScalarAsync(conn, LoanOverdueSql(scope, loanSectorExpr), Args(tenantId, userId, scope), ct, "Loans");
        }

        if (IsReady(statuses, "Protocolo"))
        {
            counts["protocolWaiting"] = await SafeScalarAsync(conn, ProtocolWaitingCountSql(scope), Args(tenantId, userId, scope), ct, "Protocolo");
        }

        if (IsReady(statuses, "Qualidade"))
            counts["documentQuality"] = await SafeScalarAsync(conn, QualityCountSql(), Args(tenantId, userId, scope), ct, "Qualidade");

        counts["criticalAlerts"] = counts["ocrError"] + counts["loanOverdue"] + counts["documentQuality"];

        var summary = BuildSummary(counts, statuses);
        return new OperationsDashboardVm
        {
            Filter = filter,
            Summary = summary,
            NextActions = BuildActions(summary),
            CriticalItems = (await GetAlertsAsync(tenantId, userId, roles, new OperationsDashboardFilter { Page = 1, PageSize = 10, OnlyCritical = true }, ct)).Items,
            ModuleStatuses = statuses,
            IsGlobalScope = scope.IsAdmin,
            IsSectorScope = scope.IsAdministradorOphir,
            IsPersonalScope = scope.IsArquivistaOphir,
            ScopeLabel = scope.Label
        };
    }

    public Task<OperationQueuePageDto> GetGedQueueAsync(Guid tenantId, Guid userId, IReadOnlyCollection<string> roles, OperationsDashboardFilter filter, CancellationToken ct)
        => LoadQueueAsync("GED", tenantId, userId, roles, filter, ct, async (conn, scope, f) =>
        {
            var sql = await GedQueueSqlAsync(conn, string.IsNullOrWhiteSpace(f.PendingType) ? "unclassified" : f.PendingType!, scope, f, false, ct);
            var countSql = await GedQueueSqlAsync(conn, string.IsNullOrWhiteSpace(f.PendingType) ? "unclassified" : f.PendingType!, scope, f, true, ct);
            return (sql, countSql, Args(tenantId, userId, scope, f));
        });

    public Task<OperationQueuePageDto> GetOcrQueueAsync(Guid tenantId, Guid userId, IReadOnlyCollection<string> roles, OperationsDashboardFilter filter, CancellationToken ct)
        => LoadQueueAsync("OCR", tenantId, userId, roles, filter, ct, async (conn, scope, f) => (OcrQueueSql(scope, false), OcrQueueSql(scope, true), Args(tenantId, userId, scope, f)));

    public Task<OperationQueuePageDto> GetPartialDocumentsQueueAsync(Guid tenantId, Guid userId, IReadOnlyCollection<string> roles, OperationsDashboardFilter filter, CancellationToken ct)
        => LoadQueueAsync("Documentos Parciais", tenantId, userId, roles, filter, ct, async (conn, scope, f) => (PartialQueueSql(scope, false), PartialQueueSql(scope, true), Args(tenantId, userId, scope, f)));

    public Task<OperationQueuePageDto> GetLoanQueueAsync(Guid tenantId, Guid userId, IReadOnlyCollection<string> roles, OperationsDashboardFilter filter, CancellationToken ct)
        => LoadQueueAsync("Loans", tenantId, userId, roles, filter, ct, async (conn, scope, f) =>
        {
            var sectorExpr = await ResolveLoanSectorSqlExpressionAsync(conn, ct);
            var itemReg = await ColumnExistsAsync(conn, "ged", "loan_request_item", "reg_status", ct) ? " and coalesce(i.reg_status,'A')='A'" : string.Empty;
            return (LoanQueueSql(scope, sectorExpr, itemReg, false), LoanQueueSql(scope, sectorExpr, itemReg, true), Args(tenantId, userId, scope, f));
        });

    public Task<OperationQueuePageDto> GetProtocolQueueAsync(Guid tenantId, Guid userId, IReadOnlyCollection<string> roles, OperationsDashboardFilter filter, CancellationToken ct)
        => LoadQueueAsync("Protocolo", tenantId, userId, roles, filter, ct, async (conn, scope, f) => (ProtocolQueueSql(scope, false), ProtocolQueueSql(scope, true), Args(tenantId, userId, scope, f)));

    public Task<OperationQueuePageDto> GetQualityQueueAsync(Guid tenantId, Guid userId, IReadOnlyCollection<string> roles, OperationsDashboardFilter filter, CancellationToken ct)
        => LoadQueueAsync("Qualidade", tenantId, userId, roles, filter, ct, async (conn, scope, f) => (QualityQueueSql(false), QualityQueueSql(true), Args(tenantId, userId, scope, f)));

    public async Task<OperationQueuePageDto> GetAlertsAsync(Guid tenantId, Guid userId, IReadOnlyCollection<string> roles, OperationsDashboardFilter filter, CancellationToken ct)
    {
        filter = NormalizeFilter(filter);
        await using var conn = await _db.OpenAsync(ct);
        var scope = await BuildScopeAsync(conn, tenantId, userId, roles, ct);
        var alerts = new List<OperationQueueItemDto>();
        async Task Add(string module, string sql)
        {
            if (!(await ModuleStatusAsync(module, ct)).IsReady) return;
            alerts.AddRange(await SafeQueryAsync<OperationQueueItemDto>(conn, sql, Args(tenantId, userId, scope, filter), ct, module));
        }
        await Add("GED", await GedAlertsSqlAsync(conn, scope, ct));
        await Add("OCR", OcrAlertsSql(scope));
        await Add("Documentos Parciais", PartialAlertsSql(scope));
        await Add("Loans", LoanAlertsSql(scope, await ResolveLoanSectorSqlExpressionAsync(conn, ct)));
        await Add("Protocolo", ProtocolAlertsSql(scope));
        await Add("Qualidade", QualityAlertsSql());
        var filtered = filter.OnlyCritical ? alerts.Where(x => x.Severity == "critical").ToList() : alerts;
        return Page(filtered.OrderByDescending(x => x.UpdatedAt ?? x.UploadedAt ?? x.DueAt ?? DateTime.MinValue).Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToList(), filter, filtered.Count, "Alertas");
    }

    private async Task<OperationQueuePageDto> LoadQueueAsync(string module, Guid tenantId, Guid userId, IReadOnlyCollection<string> roles, OperationsDashboardFilter filter, CancellationToken ct, Func<NpgsqlConnection, Scope, OperationsDashboardFilter, Task<(string Sql, string CountSql, object Args)>> build)
    {
        filter = NormalizeFilter(filter);
        var status = await ModuleStatusAsync(module, ct);
        if (!status.IsReady) return NotReady(module, filter, status.StatusText);
        await using var conn = await _db.OpenAsync(ct);
        var scope = await BuildScopeAsync(conn, tenantId, userId, roles, ct);
        try
        {
            var (sql, countSql, args) = await build(conn, scope, filter);
            var items = await SafeQueryAsync<OperationQueueItemDto>(conn, sql, args, ct, module);
            var total = await SafeScalarAsync(conn, countSql, args, ct, module);
            return Page(items, filter, total, module);
        }
        catch (Exception ex)
        {
            WarnOnce(module, ex, "Falha ao montar fila operacional {Module}.", module);
            return new OperationQueuePageDto { ModuleName = module, ModuleReady = true, Page = filter.Page, PageSize = filter.PageSize, Total = 0, Message = "Não foi possível carregar esta fila.", EmptyMessage = "Não foi possível carregar esta fila." };
        }
    }

    private static IReadOnlyList<OperationsSummaryDto> BuildSummary(IReadOnlyDictionary<string, int> c, IReadOnlyList<ModuleSchemaStatus> statuses) => new[]
    {
        Card("withoutOcr", "Documentos sem OCR", "Precisam de texto pesquisável.", c, statuses, "OCR", "secondary", "ocr", "Executar OCR"),
        Card("ocrError", "OCR com erro", "Falhas de processamento OCR.", c, statuses, "OCR", "danger", "ocr", "Ver erros OCR"),
        Card("unclassified", "Documentos sem classificação", "Aguardam tipo documental ou classificação.", c, statuses, "GED", "warning", "ged", "Classificar agora"),
        Card("incomplete", "Documentos incompletos", "Aguardam novas partes.", c, statuses, "Documentos Parciais", "danger", "partials", "Adicionar parte"),
        Card("readyToConsolidate", "Prontos para consolidar", "Partes completas aguardando consolidação.", c, statuses, "Documentos Parciais", "success", "partials", "Consolidar documentos"),
        Card("protocolWaiting", "Protocolos pendentes", "Solicitados ou aguardando análise.", c, statuses, "Protocolo", "info", "protocol", "Analisar protocolos"),
        Card("loanPending", "Loans pendentes", "Solicitações aguardando tratamento.", c, statuses, "Loans", "warning", "loans", "Aprovar solicitações"),
        Card("loanOverdue", "Empréstimos vencidos", "Itens com prazo expirado.", c, statuses, "Loans", "danger", "loans", "Cobrar devolução"),
        Card("documentQuality", "Qualidade crítica", "Documentos críticos, em atenção ou risco LGPD.", c, statuses, "Qualidade", "danger", "quality", "Ver qualidade documental"),
        Card("criticalAlerts", "Alertas críticos", "Alertas operacionais consolidados.", c, statuses, "Alertas", "dark", "alerts", "Ver alertas")
    };

    private static OperationsSummaryDto Card(string key, string title, string desc, IReadOnlyDictionary<string, int> c, IReadOnlyList<ModuleSchemaStatus> statuses, string module, string css, string tab, string action)
    {
        var status = statuses.FirstOrDefault(x => string.Equals(x.ModuleName, module, StringComparison.OrdinalIgnoreCase)) ?? new ModuleSchemaStatus { IsReady = true, ModuleName = module, StatusText = "Configurado" };
        return new OperationsSummaryDto { Key = key, Title = title, Description = desc, Count = status.IsReady ? c.GetValueOrDefault(key) : 0, CssClass = status.IsReady ? css : "secondary", Severity = css is "danger" or "dark" ? "critical" : css is "warning" ? "high" : "medium", Url = $"#{tab}", ActionLabel = status.IsReady ? action : "Não configurado", ModuleReady = status.IsReady, StatusText = status.IsReady ? null : status.StatusText, ModuleName = module };
    }

    private static IReadOnlyList<OperationActionDto> BuildActions(IEnumerable<OperationsSummaryDto> cards) => cards.Where(x => x.ModuleReady && x.Count > 0)
        .Select((x, i) => new OperationActionDto { Key = x.Key, Message = MessageFor(x), ButtonText = x.ActionLabel, Url = x.Url, Icon = IconFor(x.Key), Severity = x.Severity, Priority = x.Severity == "critical" ? i : i + 20 })
        .OrderBy(x => x.Priority).Take(8).ToList();

    private static string MessageFor(OperationsSummaryDto c) => c.Key switch
    {
        "withoutOcr" => $"{c.Count} documentos sem OCR precisam ser processados.",
        "ocrError" => $"{c.Count} documentos tiveram erro de OCR.",
        "readyToConsolidate" => $"{c.Count} documentos estão prontos para consolidar.",
        "unclassified" => $"{c.Count} documentos estão sem classificação.",
        "loanOverdue" => $"{c.Count} empréstimos estão vencidos.",
        "protocolWaiting" => $"{c.Count} protocolos aguardam análise.",
        "documentQuality" => $"{c.Count} documentos estão com qualidade crítica.",
        _ => $"{c.Count} {c.Title.ToLowerInvariant()}."
    };

    private static string IconFor(string key) => key switch
    {
        "ocrError" or "criticalAlerts" => "bi-exclamation-octagon",
        "withoutOcr" => "bi-file-earmark-text",
        "unclassified" => "bi-tags",
        "readyToConsolidate" => "bi-intersect",
        "loanOverdue" => "bi-clock-history",
        "protocolWaiting" => "bi-inboxes",
        "documentQuality" => "bi-shield-check",
        _ => "bi-lightning-charge"
    };

    private async Task<string> UnclassifiedCountSqlAsync(NpgsqlConnection conn, Scope scope, CancellationToken ct)
    {
        var typeExists = await ColumnExistsAsync(conn, "ged", "document", "type_id", ct);
        var classExists = await ColumnExistsAsync(conn, "ged", "document", "classification_id", ct);
        if (!typeExists && !classExists) return "select 0";
        var checks = new List<string>();
        if (typeExists) checks.Add("d.type_id is null");
        if (classExists) checks.Add("d.classification_id is null");
        return $"select count(*) from ged.document d where d.tenant_id=@TenantId and coalesce(d.reg_status,'A')='A' and ({string.Join(" or ", checks)}){DocumentScope(scope)}";
    }

    private async Task<string> WithoutOcrCountSqlAsync(NpgsqlConnection conn, Scope scope, CancellationToken ct)
    {
        var hasOcrText = await ColumnExistsAsync(conn, "ged", "document_version", "ocr_text", ct);
        var hasPartial = await ColumnExistsAsync(conn, "ged", "document_version", "partial_status", ct);
        var partial = hasPartial ? " and upper(coalesce(v.partial_status,'')) not like '%PARTIAL%'" : string.Empty;
        return hasOcrText ? $"select count(*) from ged.document d left join ged.document_version v on v.tenant_id=d.tenant_id and v.id=d.current_version_id where d.tenant_id=@TenantId and coalesce(d.reg_status,'A')='A' and nullif(coalesce(v.ocr_text,''),'') is null{partial}{DocumentScope(scope)}" : "select 0";
    }

    private static string OcrErrorCountSql(Scope scope) => $"select count(*) from ged.ocr_job j left join ged.document_version v on v.id=j.document_version_id left join ged.document d on d.tenant_id=j.tenant_id and (d.current_version_id=v.id or v.document_id=d.id) where j.tenant_id=@TenantId and upper(j.status::text) in ('ERROR','FAILED','FAILURE'){DocumentScope(scope)}";
    private static string PartialCountSql(Scope scope, bool ready) => $"select count(*) from ged.document d join ged.document_version v on v.tenant_id=d.tenant_id and v.id=d.current_version_id where d.tenant_id=@TenantId and coalesce(d.reg_status,'A')='A' and {(ready ? "upper(coalesce(v.partial_status,'')) in ('READY','READY_TO_CONSOLIDATE','READY_FOR_CONSOLIDATION')" : "(coalesce(v.is_document_incomplete,false) or upper(coalesce(v.partial_status,''))='INCOMPLETE')")}{DocumentScope(scope)}";
    private static string LoanPendingCountSql(Scope scope, string sectorExpr) => $"select count(*) from ged.loan_request l where l.tenant_id=@TenantId and coalesce(l.reg_status,'A')='A' and upper(l.status::text) in ('REQUESTED','SOLICITADO','PENDING','PENDENTE','RETURNED_FOR_ADJUSTMENT'){LoanScope(scope, sectorExpr)}";
    private static string LoanOverdueSql(Scope scope, string sectorExpr) => $"select count(*) from ged.loan_request l where l.tenant_id=@TenantId and coalesce(l.reg_status,'A')='A' and l.due_at < now() and upper(l.status::text) in ('APPROVED','DELIVERED','APROVADO','ENTREGUE'){LoanScope(scope, sectorExpr)}";
    private static string ProtocolWaitingCountSql(Scope scope) => $"select count(*) from ged.protocol_request p where p.tenant_id=@TenantId and coalesce(p.reg_status,'A')='A' and upper(p.status::text) in ('REQUESTED','IN_REVIEW','ADJUSTMENT_ANSWERED','NOVO','ABERTO','EM_ANALISE','EM_TRAMITACAO'){ProtocolScope(scope)}";
    private static string QualityCountSql() => "select count(*) from (select distinct on (r.tenant_id,r.document_id) r.quality_status, r.has_ocr, r.has_lgpd_risk from ged.document_quality_result r where r.tenant_id=@TenantId order by r.tenant_id,r.document_id,r.analyzed_at_utc desc) q where q.quality_status in ('Crítico','Atenção') or q.has_ocr=false or q.has_lgpd_risk=true";

    private async Task<string> GedQueueSqlAsync(NpgsqlConnection conn, string queue, Scope scope, OperationsDashboardFilter filter, bool count, CancellationToken ct)
    {
        var typeExists = await ColumnExistsAsync(conn, "ged", "document", "type_id", ct);
        var classExists = await ColumnExistsAsync(conn, "ged", "document", "classification_id", ct);
        var typeJoin = typeExists ? " left join ged.document_type dt on dt.tenant_id=d.tenant_id and dt.id=d.type_id" : string.Empty;
        var typeSelect = typeExists ? "dt.name" : "null::text";
        var classSelect = classExists ? "d.classification_id::text" : "null::text";
        var unclassified = !typeExists && !classExists ? " and false" : " and (" + string.Join(" or ", new[] { typeExists ? "d.type_id is null" : null, classExists ? "d.classification_id is null" : null }.Where(x => x is not null)) + ")";
        var where = queue switch
        {
            "withoutOcr" => " and nullif(coalesce(v.ocr_text,''),'') is null",
            "metadata" => unclassified,
            _ => unclassified
        };
        var select = count ? "select count(*)" : $"select d.id as Id, 'ged' as Queue, coalesce(d.title,d.code,'Documento') as Title, d.code as Code, f.name as Folder, {typeSelect} as DocumentType, {classSelect} as Classification, v.created_at as UploadedAt, d.status::text as Status, 'Classificar' as ActionLabel, '/Ged/Details/' || d.id as ActionUrl, 'high' as Severity";
        var page = count ? string.Empty : " order by coalesce(v.created_at,d.created_at) desc offset @Offset limit @Limit";
        return $"{select} from ged.document d left join ged.document_version v on v.tenant_id=d.tenant_id and v.id=d.current_version_id left join ged.folder f on f.tenant_id=d.tenant_id and f.id=d.folder_id{typeJoin} where d.tenant_id=@TenantId and coalesce(d.reg_status,'A')='A'{DocumentScope(scope)}{where}{page}";
    }

    private static string OcrQueueSql(Scope scope, bool count)
    {
        var select = count ? "select count(*)" : "select d.id as Id, 'ocr' as Queue, coalesce(d.title,d.code,'Documento') as Title, d.code as Code, f.name as Folder, coalesce(j.status::text, case when nullif(coalesce(v.ocr_text,''),'') is null then 'Sem OCR' else 'Concluído' end) as Status, j.requested_at as LastAttemptAt, j.error_message as Error, case when upper(coalesce(j.status::text,'')) in ('ERROR','FAILED','FAILURE') then 'Reprocessar' when nullif(coalesce(v.ocr_text,''),'') is null then 'Executar OCR' else 'Ver OCR' end as ActionLabel, case when d.id is null then '/Ocr' else '/Ged/Details/' || d.id end as ActionUrl, case when upper(coalesce(j.status::text,'')) in ('ERROR','FAILED','FAILURE') then 'critical' else 'medium' end as Severity, case when upper(coalesce(v.partial_status,'')) like '%PARTIAL%' then 'OCR parcial' else coalesce(j.status::text,'Sem OCR') end as Ocr";
        var page = count ? string.Empty : " order by coalesce(j.requested_at,v.created_at,d.created_at) desc offset @Offset limit @Limit";
        return $"{select} from ged.document d left join ged.document_version v on v.tenant_id=d.tenant_id and v.id=d.current_version_id left join ged.folder f on f.tenant_id=d.tenant_id and f.id=d.folder_id left join lateral (select * from ged.ocr_job j where j.tenant_id=d.tenant_id and j.document_version_id=v.id order by j.requested_at desc nulls last limit 1) j on true where d.tenant_id=@TenantId and coalesce(d.reg_status,'A')='A' and (nullif(coalesce(v.ocr_text,''),'') is null or upper(coalesce(j.status::text,'')) in ('PENDING','PROCESSING','ERROR','FAILED','FAILURE') or upper(coalesce(v.partial_status,'')) like '%PARTIAL%'){DocumentScope(scope)}{page}";
    }

    private static string PartialQueueSql(Scope scope, bool count)
    {
        var select = count ? "select count(*)" : "select d.id as Id, 'partials' as Queue, coalesce(d.title,d.code,'Documento') as Title, d.code as Code, f.name as Folder, coalesce(v.received_parts,0)::int as Parts, coalesce(v.expected_parts,2)::int as ExpectedParts, v.created_at as UpdatedAt, case when upper(coalesce(v.partial_status,'')) in ('READY','READY_TO_CONSOLIDATE','READY_FOR_CONSOLIDATION') or coalesce(v.received_parts,0) >= coalesce(v.expected_parts,2) then 'Pronto para consolidar' when upper(coalesce(v.partial_status,'')) like '%OCR%' then 'OCR parcial' else 'Incompleto' end as Status, case when upper(coalesce(v.partial_status,'')) in ('READY','READY_TO_CONSOLIDATE','READY_FOR_CONSOLIDATION') or coalesce(v.received_parts,0) >= coalesce(v.expected_parts,2) then 'Consolidar' else 'Ver partes' end as ActionLabel, '/Ged/Details/' || d.id as ActionUrl, case when coalesce(v.received_parts,0) < coalesce(v.expected_parts,2) then 'high' else 'medium' end as Severity, ('OCR parcial ' || coalesce(v.received_parts,0) || '/' || coalesce(v.expected_parts,2)) as Ocr";
        var page = count ? string.Empty : " order by coalesce(v.updated_at,v.created_at,d.created_at) desc offset @Offset limit @Limit";
        return $"{select} from ged.document d join ged.document_version v on v.tenant_id=d.tenant_id and v.id=d.current_version_id left join ged.folder f on f.tenant_id=d.tenant_id and f.id=d.folder_id where d.tenant_id=@TenantId and coalesce(d.reg_status,'A')='A' and (coalesce(v.is_document_incomplete,false) or nullif(coalesce(v.partial_status,''),'') is not null){DocumentScope(scope)}{page}";
    }

    private static string LoanQueueSql(Scope scope, string sectorExpr, string itemReg, bool count)
    {
        var select = count ? "select count(*)" : $"select l.id as Id, 'loans' as Queue, 'Solicitação #' || coalesce(l.protocol_no::text,l.id::text) as Title, l.protocol_no::text as Protocol, l.requester_name as Requester, {sectorExpr} as Sector, l.status::text as Status, l.due_at as DueAt, l.updated_at as UpdatedAt, (select count(*)::int from ged.loan_request_item i where i.tenant_id=l.tenant_id and i.loan_request_id=l.id{itemReg}) as ItemsCount, case when l.due_at < now() then 'Cobrar devolução' when upper(l.status::text) in ('REQUESTED','SOLICITADO','PENDING','PENDENTE') then 'Aprovar' when upper(l.status::text) in ('APPROVED','APROVADO') then 'Entregar' else 'Ver detalhes' end as ActionLabel, '/Loans/' || l.id as ActionUrl, case when l.due_at < now() then 'critical' else 'high' end as Severity";
        var page = count ? string.Empty : " order by l.due_at nulls last, coalesce(l.updated_at,l.requested_at) desc offset @Offset limit @Limit";
        return $"{select} from ged.loan_request l where l.tenant_id=@TenantId and coalesce(l.reg_status,'A')='A' and (@Status is null or @Status='' or upper(l.status::text)=upper(@Status)) and (@OnlyOverdue=false or l.due_at < now()){LoanScope(scope, sectorExpr)}{page}";
    }

    private static string ProtocolQueueSql(Scope scope, bool count)
    {
        var select = count ? "select count(*)" : "select p.id as Id, 'protocol' as Queue, p.title as Title, p.protocol_no as Protocol, p.requester_name as Requester, p.requester_sector_name as Sector, p.assigned_sector_name as DestinationSector, p.assigned_user_name as Responsible, p.status::text as Status, p.priority::text as Priority, p.due_at as DueAt, p.updated_at as UpdatedAt, case when upper(p.status::text) in ('REQUESTED','NOVO') then 'Assumir' when upper(p.status::text)='RETURNED_FOR_ADJUSTMENT' then 'Ver detalhes' when upper(p.status::text) in ('IN_REVIEW','ADJUSTMENT_ANSWERED','EM_ANALISE') then 'Analisar' when upper(p.status::text)='APPROVED' then 'Finalizar' else 'Ver detalhes' end as ActionLabel, '/Protocols/' || p.id as ActionUrl, case when p.due_at is not null and p.due_at < now() and upper(p.status::text) not in ('FINISHED','REJECTED','CANCELLED') then 'critical' when upper(p.status::text)='RETURNED_FOR_ADJUSTMENT' then 'high' else 'medium' end as Severity";
        var page = count ? string.Empty : " order by coalesce(p.updated_at,p.requested_at) desc offset @Offset limit @Limit";
        return $"{select} from ged.protocol_request p where p.tenant_id=@TenantId and coalesce(p.reg_status,'A')='A' and (@Status is null or @Status='' or upper(p.status::text)=upper(@Status)){ProtocolScope(scope)}{page}";
    }

    private static string QualityQueueSql(bool count)
    {
        var select = count ? "select count(*)" : "select q.document_id as Id, 'quality' as Queue, coalesce(d.title,d.code,'Documento') as Title, d.code as Code, q.score as Score, q.quality_status as Status, concat_ws(', ', case when q.has_ocr=false then 'Sem OCR' end, case when q.has_classification=false then 'Sem classificação' end, case when q.has_lgpd_risk=true then 'Risco LGPD' end, case when q.file_exists=false then 'Arquivo ausente' end) as PendingIssues, q.recommended_action as NextStep, q.analyzed_at_utc as LastAnalyzedAt, coalesce(q.recommended_action,'Ver qualidade') as ActionLabel, '/DocumentQuality/' || q.document_id as ActionUrl, case when q.quality_status='Crítico' then 'critical' when q.quality_status='Atenção' then 'high' else 'medium' end as Severity";
        var page = count ? string.Empty : " order by q.analyzed_at_utc desc offset @Offset limit @Limit";
        return $"{select} from (select distinct on (r.tenant_id,r.document_id) r.* from ged.document_quality_result r where r.tenant_id=@TenantId order by r.tenant_id,r.document_id,r.analyzed_at_utc desc) q left join ged.document d on d.tenant_id=q.tenant_id and d.id=q.document_id where q.quality_status in ('Crítico','Atenção') or q.has_ocr=false or q.has_classification=false or q.has_lgpd_risk=true or q.file_exists=false{page}";
    }

    private async Task<string> GedAlertsSqlAsync(NpgsqlConnection conn, Scope scope, CancellationToken ct)
    {
        var count = await UnclassifiedCountSqlAsync(conn, scope, ct);
        return count.Replace("select count(*)", "select d.id as Id, 'alerts' as Queue, 'Documento sem classificação há mais de 5 dias' as Title, d.code as Code, 'GED' as Status, d.created_at as UploadedAt, 'Classificar agora' as ActionLabel, '/Ged/Details/' || d.id as ActionUrl, 'critical' as Severity") + " and d.created_at < now() - interval '5 days' limit 20";
    }
    private static string OcrAlertsSql(Scope scope) => $"select d.id as Id, 'alerts' as Queue, 'OCR com erro há mais de 1 hora' as Title, d.code as Code, coalesce(j.error_message,'Falha OCR') as Error, j.requested_at as UpdatedAt, 'Reprocessar OCR' as ActionLabel, '/Ged/Processing?status=error' as ActionUrl, 'critical' as Severity from ged.ocr_job j left join ged.document_version v on v.id=j.document_version_id left join ged.document d on d.tenant_id=j.tenant_id and (d.current_version_id=v.id or v.document_id=d.id) where j.tenant_id=@TenantId and upper(j.status::text) in ('ERROR','FAILED','FAILURE') and j.requested_at < now() - interval '1 hour'{DocumentScope(scope)} limit 20";
    private static string PartialAlertsSql(Scope scope) => $"select d.id as Id, 'alerts' as Queue, 'Documento parcial incompleto há mais de 7 dias' as Title, d.code as Code, v.created_at as UpdatedAt, 'Ver partes' as ActionLabel, '/Ged/Details/' || d.id as ActionUrl, 'high' as Severity from ged.document d join ged.document_version v on v.tenant_id=d.tenant_id and v.id=d.current_version_id where d.tenant_id=@TenantId and coalesce(d.reg_status,'A')='A' and (coalesce(v.is_document_incomplete,false) or upper(coalesce(v.partial_status,''))='INCOMPLETE') and v.created_at < now() - interval '7 days'{DocumentScope(scope)} limit 20";
    private static string LoanAlertsSql(Scope scope, string sectorExpr) => $"select l.id as Id, 'alerts' as Queue, 'Empréstimo vencido há mais de 3 dias' as Title, l.protocol_no::text as Code, l.requester_name as Requester, {sectorExpr} as Sector, l.due_at as DueAt, 'Cobrar devolução' as ActionLabel, '/Loans/' || l.id as ActionUrl, 'critical' as Severity from ged.loan_request l where l.tenant_id=@TenantId and coalesce(l.reg_status,'A')='A' and l.due_at < now() - interval '3 days'{LoanScope(scope, sectorExpr)} limit 20";
    private static string ProtocolAlertsSql(Scope scope) => $"select p.id as Id, 'alerts' as Queue, 'Protocolo parado há mais de 48h' as Title, p.protocol_no as Code, p.requester_name as Requester, coalesce(p.assigned_sector_name,p.requester_sector_name) as Sector, p.updated_at as UpdatedAt, 'Analisar protocolos' as ActionLabel, '/Protocols/' || p.id as ActionUrl, 'high' as Severity from ged.protocol_request p where p.tenant_id=@TenantId and coalesce(p.reg_status,'A')='A' and coalesce(p.updated_at,p.requested_at) < now() - interval '48 hours' and upper(p.status::text) not in ('FINISHED','CANCELLED','REJECTED'){ProtocolScope(scope)} limit 20";
    private static string QualityAlertsSql() => "select q.document_id as Id, 'alerts' as Queue, 'Qualidade documental crítica' as Title, d.code as Code, q.quality_status as Status, q.analyzed_at_utc as UpdatedAt, 'Ver qualidade documental' as ActionLabel, '/DocumentQuality/' || q.document_id as ActionUrl, 'critical' as Severity from (select distinct on (r.tenant_id,r.document_id) r.* from ged.document_quality_result r where r.tenant_id=@TenantId order by r.tenant_id,r.document_id,r.analyzed_at_utc desc) q left join ged.document d on d.tenant_id=q.tenant_id and d.id=q.document_id where q.quality_status='Crítico' limit 20";

    private async Task<Scope> BuildScopeAsync(NpgsqlConnection conn, Guid tenantId, Guid userId, IReadOnlyCollection<string> roles, CancellationToken ct)
    {
        var normalized = roles.Select(Norm).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scope = new Scope { IsAdmin = normalized.Contains("ADMIN") || normalized.Contains("ADMINISTRADOR"), IsAdministradorOphir = normalized.Contains("ADMINISTRADOROPHIR"), IsArquivistaOphir = normalized.Contains("ARQUIVISTAOPHIR") };
        scope.Sector = await SafeStringAsync(conn, "select nullif(coalesce(s.setor, s.lotacao, ''), '') from ged.app_user u left join ged.servidor s on s.tenant_id=u.tenant_id and s.id=u.servidor_id where u.tenant_id=@TenantId and u.id=@UserId limit 1", new { TenantId = tenantId, UserId = userId }, ct);
        if (await TableExistsAsync(conn, "ged", "protocolo_usuario_setor", ct))
            scope.SectorIds = (await SafeQueryAsync<Guid>(conn, "select us.setor_id from ged.protocolo_usuario_setor us where us.tenant_id=@TenantId and us.usuario_id=@UserId and coalesce(us.reg_status,'A')='A' and coalesce(us.ativo,true)=true", new { TenantId = tenantId, UserId = userId }, ct, "Protocolo")).ToArray();
        scope.Label = scope.IsAdmin ? "Visão global" : scope.IsAdministradorOphir ? $"Setor: {scope.Sector ?? "não vinculado"}" : "Meus itens e solicitações";
        return scope;
    }

    private async Task<string> ResolveLoanSectorSqlExpressionAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        foreach (var column in new[] { "requester_sector", "requester_sector_name", "sector_name", "requesting_sector" })
            if (await ColumnExistsAsync(conn, "ged", "loan_request", column, ct)) return $"l.{column}";
        return "null::text";
    }

    private async Task<ModuleSchemaStatus> ModuleStatusAsync(string module, CancellationToken ct) => await _schema.GetModuleStatusAsync(module, ct);
    private static bool IsReady(IEnumerable<ModuleSchemaStatus> statuses, string module) => statuses.FirstOrDefault(x => string.Equals(x.ModuleName, module, StringComparison.OrdinalIgnoreCase))?.IsReady ?? false;
    private async Task<bool> TableExistsAsync(NpgsqlConnection conn, string schema, string table, CancellationToken ct) => await conn.ExecuteScalarAsync<bool>(new CommandDefinition("select exists (select 1 from information_schema.tables where table_schema=@schema and table_name=@table)", new { schema, table }, cancellationToken: ct));
    private async Task<bool> ColumnExistsAsync(NpgsqlConnection conn, string schema, string table, string column, CancellationToken ct) => await conn.ExecuteScalarAsync<bool>(new CommandDefinition("select exists (select 1 from information_schema.columns where table_schema=@schema and table_name=@table and column_name=@column)", new { schema, table, column }, cancellationToken: ct));
    private static OperationsDashboardFilter NormalizeFilter(OperationsDashboardFilter? filter) { filter ??= new(); filter.Page = Math.Max(1, filter.Page); filter.PageSize = Math.Clamp(filter.PageSize <= 0 ? 10 : filter.PageSize, 1, 50); return filter; }
    private static object Args(Guid tenantId, Guid userId, Scope scope, OperationsDashboardFilter? filter = null) => new { TenantId = tenantId, UserId = userId, SectorIds = scope.SectorIds, Sector = scope.Sector, Status = filter?.Status, OnlyOverdue = filter?.OnlyOverdue ?? false, Offset = ((filter?.Page ?? 1) - 1) * (filter?.PageSize ?? 10), Limit = filter?.PageSize ?? 10 };
    private static OperationQueuePageDto Page(IReadOnlyList<OperationQueueItemDto> items, OperationsDashboardFilter filter, int total, string module) => new() { Items = items, Page = filter.Page, PageSize = filter.PageSize, Total = total, ModuleName = module, ModuleReady = true };
    private static OperationQueuePageDto NotReady(string module, OperationsDashboardFilter filter, string? message) => new() { Items = Array.Empty<OperationQueueItemDto>(), Page = filter.Page, PageSize = filter.PageSize, Total = 0, ModuleName = module, ModuleReady = false, Message = message, EmptyMessage = message ?? $"Módulo {module} ainda não configurado." };
    private async Task<int> SafeScalarAsync(NpgsqlConnection conn, string sql, object? args, CancellationToken ct, string module) { try { return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, args, cancellationToken: ct)); } catch (Exception ex) { WarnOnce(module, ex, "OPERATIONS_QUERY_FAILED scalar {Module}: {Sql}", module, sql); return 0; } }
    private async Task<string?> SafeStringAsync(NpgsqlConnection conn, string sql, object? args, CancellationToken ct) { try { return await conn.ExecuteScalarAsync<string?>(new CommandDefinition(sql, args, cancellationToken: ct)); } catch { return null; } }
    private async Task<IReadOnlyList<T>> SafeQueryAsync<T>(NpgsqlConnection conn, string sql, object? args, CancellationToken ct, string module) { try { return (await conn.QueryAsync<T>(new CommandDefinition(sql, args, cancellationToken: ct))).ToList(); } catch (Exception ex) { WarnOnce(module, ex, "OPERATIONS_QUERY_FAILED query {Module}: {Sql}", module, sql); return Array.Empty<T>(); } }
    private void WarnOnce(string module, Exception ex, string message, params object?[] args) { if (Warned.TryAdd($"{module}:{ex.GetType().Name}:{ex.Message}", true)) _logger.LogWarning(ex, message, args); }
    private static string DocumentScope(Scope scope) => scope.IsAdmin ? string.Empty : scope.IsArquivistaOphir ? " and d.created_by=@UserId" : string.Empty;
    private static string LoanScope(Scope scope, string sectorExpr) => scope.IsAdmin ? string.Empty : scope.IsAdministradorOphir && sectorExpr != "null::text" ? $" and nullif(coalesce({sectorExpr},''),'') = @Sector" : scope.IsAdministradorOphir ? string.Empty : " and l.requester_id=@UserId";
    private static string ProtocolScope(Scope scope) => scope.IsAdmin ? string.Empty : " and ((@Sector is not null and (p.assigned_sector_name=@Sector or p.requester_sector_name=@Sector)) or p.assigned_user_id=@UserId or p.requester_user_id=@UserId)";
    private static string Norm(string? value) => (value ?? string.Empty).Trim().Replace(" ", "").Replace("_", "").Replace("-", "").ToUpperInvariant();

    private sealed class Scope
    {
        public bool IsAdmin { get; set; }
        public bool IsAdministradorOphir { get; set; }
        public bool IsArquivistaOphir { get; set; }
        public string? Sector { get; set; }
        public Guid[] SectorIds { get; set; } = Array.Empty<Guid>();
        public string? Label { get; set; }
    }
}
