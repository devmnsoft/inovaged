using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using Dapper;
using InovaGed.Application.Billing;
using InovaGed.Application.Common.Database;

namespace InovaGed.Infrastructure.Billing;
public sealed class BillingCommands(IDbConnectionFactory db) : IBillingCommands
{
    public async Task SaveExtractionAsync(Guid tenantId, BillingExtractionDto e, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        const string sql = """
insert into ged.billing_document_extraction(tenant_id,document_id,document_version_id,extraction_status,document_kind,supplier_name,supplier_document,invoice_number,invoice_series,issue_date,due_date,competence_month,gross_amount,net_amount,tax_amount,iss_amount,inss_amount,pis_amount,cofins_amount,ir_amount,csll_amount,contract_number,purchase_order,cost_center,service_description,ust_quantity,ust_unit_value,confidence,extracted_json,warnings_json)
values(@tenantId,@DocumentId,@DocumentVersionId,@ExtractionStatus,@DocumentKind,@SupplierName,@SupplierDocument,@InvoiceNumber,@InvoiceSeries,@IssueDate,@DueDate,@CompetenceMonth,@GrossAmount,@NetAmount,@TaxAmount,@IssAmount,@InssAmount,@PisAmount,@CofinsAmount,@IrAmount,@CsllAmount,@ContractNumber,@PurchaseOrder,@CostCenter,@ServiceDescription,@UstQuantity,@UstUnitValue,@Confidence,cast(@json as jsonb),cast(@warnings as jsonb))
on conflict(tenant_id,document_id) where reg_status='A' do update set
document_version_id=case when ged.billing_document_extraction.reviewed_at is null then excluded.document_version_id else ged.billing_document_extraction.document_version_id end,
document_kind=case when ged.billing_document_extraction.reviewed_at is null then excluded.document_kind else ged.billing_document_extraction.document_kind end,
supplier_name=case when ged.billing_document_extraction.reviewed_at is null then excluded.supplier_name else ged.billing_document_extraction.supplier_name end,
supplier_document=case when ged.billing_document_extraction.reviewed_at is null then excluded.supplier_document else ged.billing_document_extraction.supplier_document end,
invoice_number=case when ged.billing_document_extraction.reviewed_at is null then excluded.invoice_number else ged.billing_document_extraction.invoice_number end,
invoice_series=case when ged.billing_document_extraction.reviewed_at is null then excluded.invoice_series else ged.billing_document_extraction.invoice_series end,
issue_date=case when ged.billing_document_extraction.reviewed_at is null then excluded.issue_date else ged.billing_document_extraction.issue_date end,
due_date=case when ged.billing_document_extraction.reviewed_at is null then excluded.due_date else ged.billing_document_extraction.due_date end,
competence_month=case when ged.billing_document_extraction.reviewed_at is null then excluded.competence_month else ged.billing_document_extraction.competence_month end,
gross_amount=case when ged.billing_document_extraction.reviewed_at is null then excluded.gross_amount else ged.billing_document_extraction.gross_amount end,
net_amount=case when ged.billing_document_extraction.reviewed_at is null then excluded.net_amount else ged.billing_document_extraction.net_amount end,
tax_amount=case when ged.billing_document_extraction.reviewed_at is null then excluded.tax_amount else ged.billing_document_extraction.tax_amount end,
iss_amount=case when ged.billing_document_extraction.reviewed_at is null then excluded.iss_amount else ged.billing_document_extraction.iss_amount end,
inss_amount=case when ged.billing_document_extraction.reviewed_at is null then excluded.inss_amount else ged.billing_document_extraction.inss_amount end,
pis_amount=case when ged.billing_document_extraction.reviewed_at is null then excluded.pis_amount else ged.billing_document_extraction.pis_amount end,
cofins_amount=case when ged.billing_document_extraction.reviewed_at is null then excluded.cofins_amount else ged.billing_document_extraction.cofins_amount end,
ir_amount=case when ged.billing_document_extraction.reviewed_at is null then excluded.ir_amount else ged.billing_document_extraction.ir_amount end,
csll_amount=case when ged.billing_document_extraction.reviewed_at is null then excluded.csll_amount else ged.billing_document_extraction.csll_amount end,
contract_number=case when ged.billing_document_extraction.reviewed_at is null then excluded.contract_number else ged.billing_document_extraction.contract_number end,
purchase_order=case when ged.billing_document_extraction.reviewed_at is null then excluded.purchase_order else ged.billing_document_extraction.purchase_order end,
cost_center=case when ged.billing_document_extraction.reviewed_at is null then excluded.cost_center else ged.billing_document_extraction.cost_center end,
service_description=case when ged.billing_document_extraction.reviewed_at is null then excluded.service_description else ged.billing_document_extraction.service_description end,
ust_quantity=case when ged.billing_document_extraction.reviewed_at is null then excluded.ust_quantity else ged.billing_document_extraction.ust_quantity end,
ust_unit_value=case when ged.billing_document_extraction.reviewed_at is null then excluded.ust_unit_value else ged.billing_document_extraction.ust_unit_value end,
confidence=case when ged.billing_document_extraction.reviewed_at is null then excluded.confidence else ged.billing_document_extraction.confidence end,
extracted_json=case when ged.billing_document_extraction.reviewed_at is null then excluded.extracted_json else ged.billing_document_extraction.extracted_json end,
warnings_json=case when ged.billing_document_extraction.reviewed_at is null then excluded.warnings_json else ged.billing_document_extraction.warnings_json end,
extraction_status=case when ged.billing_document_extraction.reviewed_at is null then 'PENDING_REVIEW' else ged.billing_document_extraction.extraction_status end,

pending_candidate_json=case when ged.billing_document_extraction.reviewed_at is null then null else excluded.extracted_json end,

revision=ged.billing_document_extraction.revision+1,updated_at=now()
returning id

""";
        var extractionId = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, new { tenantId, e.DocumentId, e.DocumentVersionId, e.ExtractionStatus, e.DocumentKind, e.SupplierName, e.SupplierDocument, e.InvoiceNumber, e.InvoiceSeries, e.IssueDate, e.DueDate, e.CompetenceMonth, e.GrossAmount, e.NetAmount, e.TaxAmount, e.IssAmount, e.InssAmount, e.PisAmount, e.CofinsAmount, e.IrAmount, e.CsllAmount, e.ContractNumber, e.PurchaseOrder, e.CostCenter, e.ServiceDescription, e.UstQuantity, e.UstUnitValue, e.Confidence, json = JsonSerializer.Serialize(e), warnings = JsonSerializer.Serialize(e.Warnings) }, tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.billing_extraction_candidate(tenant_id,extraction_id,document_version_id,snapshot_json,fingerprint)
values(@tenantId,@extractionId,@DocumentVersionId,cast(@json as jsonb),md5(cast(cast(@json as jsonb) as text)))
on conflict(tenant_id,extraction_id,fingerprint) do nothing
""", new { tenantId, extractionId, e.DocumentVersionId, json = JsonSerializer.Serialize(e) }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }

    public async Task<bool> ReviewAsync(Guid tenantId, Guid extractionId, Guid userId, BillingReviewInput i, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(i, new ValidationContext(i), errors, true)) return false;
        await using var tx = await conn.BeginTransactionAsync(ct);
        var stored = await conn.QuerySingleOrDefaultAsync<BillingExtractionDto>(new CommandDefinition($"""
select {BillingQueries.Projection} from ged.billing_document_extraction e
left join ged.document d on d.tenant_id=e.tenant_id and d.id=e.document_id
where e.tenant_id=@tenantId and e.id=@extractionId and e.reg_status='A' for update of e
""", new { tenantId, extractionId }, tx, cancellationToken: ct));
        if (stored is null || stored.Revision != i.Revision) return false;
        var source = i.UseCandidate && stored.PendingCandidateJson is not null
            ? JsonSerializer.Deserialize<BillingExtractionDto>(stored.PendingCandidateJson) : stored;
        if (source is null || (i.UseCandidate && stored.PendingCandidateJson is null)) return false;
        var duplicates = await conn.ExecuteScalarAsync<int>(new CommandDefinition("""
select count(*)::int from ged.billing_document_extraction du left join ged.billing_duplicate_policy dp on dp.tenant_id=du.tenant_id
where du.tenant_id=@tenantId and du.id<>@extractionId and du.reg_status='A'
and nullif(regexp_replace(cast(@SupplierDocument as text),'\D','','g'),'')=regexp_replace(du.supplier_document,'\D','','g')
and nullif(cast(@InvoiceNumber as text),'')=du.invoice_number
and (not coalesce(dp.compare_series,true) or coalesce(cast(@InvoiceSeries as text),'')=coalesce(du.invoice_series,''))
and (not coalesce(dp.compare_competence,true) or coalesce(cast(@CompetenceMonth as text),'')=coalesce(du.competence_month,''))
and (not coalesce(dp.compare_amount,false) or cast(@GrossAmount as numeric)=du.gross_amount)
""",new {tenantId,extractionId,i.SupplierDocument,i.InvoiceNumber,i.InvoiceSeries,i.CompetenceMonth,i.GrossAmount},tx,cancellationToken:ct));
        if (!i.HasDivergence && (!BillingChecklist.CanApprove(BillingChecklist.EvaluateReview(source, i))
            || source.Warnings.Any(w => w.StartsWith("Regra obrigatória", StringComparison.OrdinalIgnoreCase))
            || (duplicates > 0 && !i.ConfirmDuplicate))) return false;
        if (i.ResponsibleUserId.HasValue && !await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists(select 1 from ged.app_user where tenant_id=@tenantId and id=@responsible and coalesce(reg_status,'A')='A' and is_active)",
            new { tenantId, responsible = i.ResponsibleUserId }, tx, cancellationToken: ct))) return false;
        var scheduled = await conn.ExecuteScalarAsync<decimal>(new CommandDefinition("select coalesce(sum(amount),0) from ged.billing_installment where tenant_id=@tenantId and extraction_id=@extractionId", new { tenantId, extractionId }, tx, cancellationToken: ct));
        if (scheduled > (i.NetAmount ?? i.GrossAmount ?? 0)) return false;
        const string sql = """
update ged.billing_document_extraction set supplier_name=@SupplierName,supplier_document=@SupplierDocument,invoice_number=@InvoiceNumber,invoice_series=@InvoiceSeries,issue_date=@IssueDate,due_date=@DueDate,competence_month=@CompetenceMonth,gross_amount=@GrossAmount,net_amount=@NetAmount,tax_amount=@TaxAmount,contract_number=@ContractNumber,purchase_order=@PurchaseOrder,cost_center=@CostCenter,service_description=@ServiceDescription,ust_quantity=@UstQuantity,ust_unit_value=@UstUnitValue,
extraction_status=case when @HasDivergence then 'DIVERGENT' else 'APPROVED' end,
reviewed_by=@userId,reviewed_at=clock_timestamp(),updated_at=now(),revision=revision+1,
responsible_user_id=@ResponsibleUserId,review_due_date=@ReviewDueDate,priority=@Priority,divergence_reason=@DivergenceReason,review_notes=@Notes,
document_version_id=@DocumentVersionId,document_kind=@DocumentKind,confidence=@Confidence,
iss_amount=@IssAmount,inss_amount=@InssAmount,pis_amount=@PisAmount,cofins_amount=@CofinsAmount,ir_amount=@IrAmount,csll_amount=@CsllAmount,
extracted_json=case when @UseCandidate then cast(@SourceJson as jsonb) else extracted_json end,

warnings_json=cast(@WarningsJson as jsonb),pending_candidate_json=case when @UseCandidate then null else pending_candidate_json end
where tenant_id=@tenantId and id=@extractionId and reg_status='A' and revision=@Revision
""";
        var affected = await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, extractionId, userId, i.SupplierName, i.SupplierDocument, i.InvoiceNumber, i.InvoiceSeries, i.IssueDate, i.DueDate, i.CompetenceMonth, i.GrossAmount, i.NetAmount, i.TaxAmount, i.ContractNumber, i.PurchaseOrder, i.CostCenter, i.ServiceDescription, i.UstQuantity, i.UstUnitValue, i.HasDivergence, i.Revision, i.ResponsibleUserId, i.ReviewDueDate, i.Priority, i.DivergenceReason, i.Notes, i.UseCandidate, source.DocumentVersionId, source.DocumentKind, source.Confidence,source.IssAmount,source.InssAmount,source.PisAmount,source.CofinsAmount,source.IrAmount,source.CsllAmount, SourceJson = stored.PendingCandidateJson, WarningsJson = JsonSerializer.Serialize(source.Warnings) }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return affected == 1;
    }
}
