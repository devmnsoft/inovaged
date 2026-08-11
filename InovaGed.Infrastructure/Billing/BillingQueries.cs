using Dapper;
using InovaGed.Application.Billing;
using InovaGed.Application.Common.Database;

namespace InovaGed.Infrastructure.Billing;
public sealed class BillingQueries(IDbConnectionFactory db) : IBillingQueries
{
    public async Task<(BillingKpis Kpis, IReadOnlyList<BillingExtractionDto> Rows)> DashboardAsync(Guid tenantId, BillingFilter filter, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        const string where = """
where e.tenant_id=@tenantId and e.reg_status='A'
and (@Supplier is null or e.supplier_name ilike '%'||@Supplier||'%')
and (@SupplierDocument is null or regexp_replace(coalesce(e.supplier_document,''),'\D','','g') like '%'||regexp_replace(@SupplierDocument,'\D','','g')||'%')
and (@Competence is null or e.competence_month=@Competence)
and (@Status is null or e.extraction_status=@Status)
and (@MinimumAmount is null or e.gross_amount>=@MinimumAmount)
""";
        var args = new { tenantId, filter.Supplier, filter.SupplierDocument, filter.Competence, filter.Status, filter.MinimumAmount };
        var rows = (await conn.QueryAsync<BillingExtractionDto>(new CommandDefinition($$"""
select e.id "Id", e.document_id "DocumentId", e.document_version_id "DocumentVersionId", coalesce(d.title,d.code,'Documento') "DocumentTitle",
e.extraction_status "ExtractionStatus", e.document_kind "DocumentKind", e.supplier_name "SupplierName", e.supplier_document "SupplierDocument",
e.invoice_number "InvoiceNumber", e.invoice_series "InvoiceSeries", e.issue_date "IssueDate", e.due_date "DueDate", e.competence_month "CompetenceMonth",
e.gross_amount "GrossAmount", e.net_amount "NetAmount", e.tax_amount "TaxAmount", e.iss_amount "IssAmount", e.inss_amount "InssAmount",
e.pis_amount "PisAmount", e.cofins_amount "CofinsAmount", e.ir_amount "IrAmount", e.csll_amount "CsllAmount", e.contract_number "ContractNumber",
e.purchase_order "PurchaseOrder", e.cost_center "CostCenter", e.service_description "ServiceDescription", e.ust_quantity "UstQuantity",
e.ust_unit_value "UstUnitValue", e.confidence "Confidence", e.created_at "CreatedAt", e.reviewed_at "ReviewedAt"
from ged.billing_document_extraction e left join ged.document d on d.tenant_id=e.tenant_id and d.id=e.document_id
{{where}} order by e.created_at desc limit 300
""", args, cancellationToken: ct))).AsList();
        var k = await conn.QuerySingleAsync<BillingKpis>(new CommandDefinition("""
select count(*)::int "Extracted", count(*) filter(where extraction_status='PENDING_REVIEW')::int "PendingReview",
count(*) filter(where extraction_status='APPROVED')::int "Approved", count(*) filter(where extraction_status='DIVERGENT')::int "Divergent",
coalesce(sum(gross_amount),0) "TotalAmount",
(select count(*)::int from ged.document d where d.tenant_id=@tenantId and d.reg_status='A' and not exists(select 1 from ged.document_search ds where ds.tenant_id=d.tenant_id and ds.document_id=d.id and nullif(btrim(ds.ocr_text),'') is not null)) "WithoutOcr"
from ged.billing_document_extraction where tenant_id=@tenantId and reg_status='A'
""", new { tenantId }, cancellationToken: ct));
        return (k, rows);
    }

    public async Task<BillingExtractionDto?> GetAsync(Guid tenantId, Guid id, CancellationToken ct)
    {
        var (_, rows) = await DashboardAsync(tenantId, new BillingFilter(), ct);
        return rows.FirstOrDefault(x => x.Id == id);
    }
}
