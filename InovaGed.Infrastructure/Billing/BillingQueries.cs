using Dapper;
using InovaGed.Application.Billing;
using InovaGed.Application.Common.Database;

namespace InovaGed.Infrastructure.Billing;
public sealed class BillingQueries(IDbConnectionFactory db) : IBillingQueries
{
    internal const string Projection = """
e.id "Id", e.document_id "DocumentId", e.document_version_id "DocumentVersionId", coalesce(d.title,d.code,'Documento') "DocumentTitle",
e.processing_status "ProcessingStatus",e.extraction_status "ExtractionStatus", e.document_kind "DocumentKind", e.supplier_name "SupplierName", e.supplier_document "SupplierDocument",
e.invoice_number "InvoiceNumber", e.invoice_series "InvoiceSeries", e.issue_date "IssueDate", e.due_date "DueDate", e.competence_month "CompetenceMonth",
e.gross_amount "GrossAmount", e.net_amount "NetAmount", e.tax_amount "TaxAmount", e.iss_amount "IssAmount", e.inss_amount "InssAmount",
e.pis_amount "PisAmount", e.cofins_amount "CofinsAmount", e.ir_amount "IrAmount", e.csll_amount "CsllAmount", e.contract_number "ContractNumber",
e.purchase_order "PurchaseOrder", e.cost_center "CostCenter", e.service_description "ServiceDescription", e.ust_quantity "UstQuantity",
e.ust_unit_value "UstUnitValue", e.confidence "Confidence", e.created_at "CreatedAt", e.reviewed_at "ReviewedAt",
e.warnings_json::text "WarningsJson",e.revision "Revision",e.pending_candidate_json::text "PendingCandidateJson",e.extracted_json::text "ExtractedJson",
e.responsible_user_id "ResponsibleUserId",e.review_due_date "ReviewDueDate",e.priority "Priority",e.divergence_reason "DivergenceReason",
(select count(*)::int from ged.billing_document_extraction du left join ged.billing_duplicate_policy dp on dp.tenant_id=du.tenant_id
 where du.tenant_id=e.tenant_id and du.reg_status='A' and du.id<>e.id
 and nullif(regexp_replace(e.supplier_document,'\D','','g'),'')=regexp_replace(du.supplier_document,'\D','','g')
 and nullif(e.invoice_number,'')=du.invoice_number
 and (not coalesce(dp.compare_series,true) or coalesce(e.invoice_series,'')=coalesce(du.invoice_series,''))
 and (not coalesce(dp.compare_competence,true) or coalesce(e.competence_month,'')=coalesce(du.competence_month,''))
 and (not coalesce(dp.compare_amount,false) or e.gross_amount=du.gross_amount)) "DuplicateCount"
""";

    private const string WhereClause = """
where e.tenant_id=@tenantId and e.reg_status='A'
and (@Supplier is null or e.supplier_name ilike '%'||@Supplier||'%')
and (@SupplierDocument is null or regexp_replace(coalesce(e.supplier_document,''),'\D','','g') like '%'||regexp_replace(@SupplierDocument,'\D','','g')||'%')
and (@Competence is null or e.competence_month=@Competence)
and (@Status is null or e.extraction_status=@Status or (@Status='PENDING_REVIEW' and e.pending_candidate_json is not null))
and (@MinimumAmount is null or e.gross_amount>=@MinimumAmount)
and (@ResponsibleUserId is null or e.responsible_user_id=@ResponsibleUserId)
and (@Overdue is null or (coalesce(e.review_due_date<current_date,false) and (e.extraction_status<>'APPROVED' or e.pending_candidate_json is not null))=@Overdue)
""";

    private static object Arguments(Guid tenantId, BillingFilter filter) => new { tenantId, filter.Supplier, filter.SupplierDocument, filter.Competence, filter.Status, filter.MinimumAmount, filter.ResponsibleUserId, filter.Overdue, pageSize = Math.Clamp(filter.PageSize, 1, 200), offset = ((long)Math.Max(1, filter.Page)-1)*Math.Clamp(filter.PageSize, 1, 200) };

    public async Task<(BillingKpis Kpis, IReadOnlyList<BillingExtractionDto> Rows)> DashboardAsync(Guid tenantId, BillingFilter filter, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);

        var args = Arguments(tenantId, filter);
        var searchColumns = (await conn.QueryAsync<string>(new CommandDefinition("select column_name from information_schema.columns where table_schema='ged' and table_name='document_search'", cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var textColumn = searchColumns.Contains("ocr_text") ? "ocr_text" : searchColumns.Contains("content") ? "content" : searchColumns.Contains("search_text") ? "search_text" : null;
        var withoutOcrSql = textColumn is null ? "count(*)::int" : $"count(*) filter(where not exists(select 1 from ged.document_search ds where ds.tenant_id=e.tenant_id and ds.document_id=e.document_id and nullif(btrim(ds.{textColumn}),'') is not null))::int";
        var rows = (await conn.QueryAsync<BillingExtractionDto>(new CommandDefinition($$"""
select {{Projection}}
from ged.billing_document_extraction e left join ged.document d on d.tenant_id=e.tenant_id and d.id=e.document_id
{{WhereClause}} order by e.priority desc,e.review_due_date asc nulls last,e.created_at desc,e.id desc limit @pageSize offset @offset
""", args, cancellationToken: ct))).AsList();
        var k = await conn.QuerySingleAsync<BillingKpis>(new CommandDefinition($"""
select count(*)::int "Extracted", count(*) filter(where extraction_status='PENDING_REVIEW' or pending_candidate_json is not null)::int "PendingReview",
count(*) filter(where extraction_status='APPROVED')::int "Approved", count(*) filter(where extraction_status='DIVERGENT')::int "Divergent",
coalesce(sum(gross_amount),0) "TotalAmount",
{withoutOcrSql} "WithoutOcr"
from ged.billing_document_extraction e
{WhereClause}
""", args, cancellationToken: ct));
        return (k, rows);
    }

    public async Task<IReadOnlyList<BillingExtractionDto>> ExportAsync(Guid tenantId, BillingFilter filter, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return (await conn.QueryAsync<BillingExtractionDto>(new CommandDefinition($"""
select {Projection} from ged.billing_document_extraction e
left join ged.document d on d.tenant_id=e.tenant_id and d.id=e.document_id
{WhereClause} order by e.priority desc,e.review_due_date asc nulls last,e.created_at desc,e.id desc
""", Arguments(tenantId,filter), cancellationToken:ct))).AsList();
    }

    public async Task<BillingExtractionDto?> GetAsync(Guid tenantId, Guid id, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<BillingExtractionDto>(new CommandDefinition($"""
select {Projection} from ged.billing_document_extraction e
left join ged.document d on d.tenant_id=e.tenant_id and d.id=e.document_id
where e.tenant_id=@tenantId and e.id=@id and e.reg_status='A'
""", new { tenantId, id }, cancellationToken: ct));
    }
}
