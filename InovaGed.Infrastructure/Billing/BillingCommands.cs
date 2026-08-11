using System.Text.Json;
using Dapper;
using InovaGed.Application.Billing;
using InovaGed.Application.Common.Database;

namespace InovaGed.Infrastructure.Billing;
public sealed class BillingCommands(IDbConnectionFactory db) : IBillingCommands
{
    public async Task SaveExtractionAsync(Guid tenantId, BillingExtractionDto e, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        const string sql = """
insert into ged.billing_document_extraction(tenant_id,document_id,document_version_id,extraction_status,document_kind,supplier_name,supplier_document,invoice_number,invoice_series,issue_date,due_date,competence_month,gross_amount,net_amount,tax_amount,iss_amount,inss_amount,pis_amount,cofins_amount,ir_amount,csll_amount,contract_number,purchase_order,cost_center,service_description,ust_quantity,ust_unit_value,confidence,extracted_json,warnings_json)
values(@tenantId,@DocumentId,@DocumentVersionId,@ExtractionStatus,@DocumentKind,@SupplierName,@SupplierDocument,@InvoiceNumber,@InvoiceSeries,@IssueDate,@DueDate,@CompetenceMonth,@GrossAmount,@NetAmount,@TaxAmount,@IssAmount,@InssAmount,@PisAmount,@CofinsAmount,@IrAmount,@CsllAmount,@ContractNumber,@PurchaseOrder,@CostCenter,@ServiceDescription,@UstQuantity,@UstUnitValue,@Confidence,cast(@json as jsonb),cast(@warnings as jsonb))
on conflict(tenant_id,document_id) where reg_status='A' do update set document_version_id=excluded.document_version_id, extraction_status='PENDING_REVIEW', document_kind=excluded.document_kind,supplier_document=excluded.supplier_document,invoice_number=excluded.invoice_number,invoice_series=excluded.invoice_series,issue_date=excluded.issue_date,due_date=excluded.due_date,competence_month=excluded.competence_month,gross_amount=excluded.gross_amount,net_amount=excluded.net_amount,tax_amount=excluded.tax_amount,iss_amount=excluded.iss_amount,inss_amount=excluded.inss_amount,pis_amount=excluded.pis_amount,cofins_amount=excluded.cofins_amount,ir_amount=excluded.ir_amount,csll_amount=excluded.csll_amount,contract_number=excluded.contract_number,purchase_order=excluded.purchase_order,cost_center=excluded.cost_center,service_description=excluded.service_description,ust_quantity=excluded.ust_quantity,ust_unit_value=excluded.ust_unit_value,confidence=excluded.confidence,extracted_json=excluded.extracted_json,warnings_json=excluded.warnings_json,updated_at=now()
""";
        await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, e.DocumentId, e.DocumentVersionId, e.ExtractionStatus, e.DocumentKind, e.SupplierName, e.SupplierDocument, e.InvoiceNumber, e.InvoiceSeries, e.IssueDate, e.DueDate, e.CompetenceMonth, e.GrossAmount, e.NetAmount, e.TaxAmount, e.IssAmount, e.InssAmount, e.PisAmount, e.CofinsAmount, e.IrAmount, e.CsllAmount, e.ContractNumber, e.PurchaseOrder, e.CostCenter, e.ServiceDescription, e.UstQuantity, e.UstUnitValue, e.Confidence, json = JsonSerializer.Serialize(e), warnings = JsonSerializer.Serialize(e.Warnings) }, cancellationToken: ct));
    }

    public async Task<bool> ReviewAsync(Guid tenantId, Guid extractionId, Guid userId, BillingReviewInput i, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        const string sql = """
update ged.billing_document_extraction set supplier_name=@SupplierName,supplier_document=@SupplierDocument,invoice_number=@InvoiceNumber,invoice_series=@InvoiceSeries,issue_date=@IssueDate,due_date=@DueDate,competence_month=@CompetenceMonth,gross_amount=@GrossAmount,net_amount=@NetAmount,tax_amount=@TaxAmount,contract_number=@ContractNumber,purchase_order=@PurchaseOrder,cost_center=@CostCenter,service_description=@ServiceDescription,ust_quantity=@UstQuantity,ust_unit_value=@UstUnitValue,extraction_status=case when @HasDivergence then 'DIVERGENT' else 'APPROVED' end,reviewed_by=@userId,reviewed_at=now(),updated_at=now()
where tenant_id=@tenantId and id=@extractionId and reg_status='A'
""";
        return await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, extractionId, userId, i.SupplierName, i.SupplierDocument, i.InvoiceNumber, i.InvoiceSeries, i.IssueDate, i.DueDate, i.CompetenceMonth, i.GrossAmount, i.NetAmount, i.TaxAmount, i.ContractNumber, i.PurchaseOrder, i.CostCenter, i.ServiceDescription, i.UstQuantity, i.UstUnitValue, i.HasDivergence }, cancellationToken: ct)) == 1;
    }
}
