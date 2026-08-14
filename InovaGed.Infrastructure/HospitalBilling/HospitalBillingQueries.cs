using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.HospitalBilling;

namespace InovaGed.Infrastructure.HospitalBilling;

public sealed class HospitalBillingQueries(IDbConnectionFactory db) : IHospitalBillingQueries
{
    public async Task<HospitalBillingDashboard> DashboardAsync(Guid tenantId, HospitalBillingFilter filter, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        const string where = """where h.tenant_id=@tenantId and h.reg_status='A' and (@Insurer is null or h.insurer ilike '%'||@Insurer||'%') and (@Competence is null or h.competence=@Competence) and (@Status is null or h.review_status=@Status) and (@HasDenial is null or (h.denied_amount>0)=@HasDenial) and (@Term is null or concat_ws(' ',h.insurer,h.provider_name,h.provider_cnpj,h.cnes,h.guide_number,h.authorization_number,h.batch_number,h.invoice_number,h.procedure_name,h.procedure_code,h.denial_reason) ilike '%'||@Term||'%')""";
        var args = new { tenantId, filter.Insurer, filter.Competence, filter.Status, filter.HasDenial, filter.Term };
        var documents = (await connection.QueryAsync<HospitalBillingDocumentDto>(new CommandDefinition($$"""
select h.id "Id",h.document_id "DocumentId",coalesce(d.title,d.code,'Documento hospitalar') "Title",h.document_type "DocumentType",h.insurer "Insurer",h.provider_name "Provider",h.provider_cnpj "ProviderCnpj",h.cnes "Cnes",h.guide_number "GuideNumber",h.authorization_number "AuthorizationNumber",h.batch_number "BatchNumber",h.invoice_number "InvoiceNumber",h.competence "Competence",h.procedure_name "ProcedureName",h.procedure_code "ProcedureCode",
case when nullif(h.patient_name,'') is null then 'Dado protegido' else left(h.patient_name,1)||repeat('*',greatest(length(h.patient_name)-2,3))||right(h.patient_name,1) end "MaskedPatient",
h.presented_amount "PresentedAmount",h.approved_amount "ApprovedAmount",h.denied_amount "DeniedAmount",h.recovered_amount "RecoveredAmount",h.confidence "Confidence",h.review_status "Status",h.denial_reason "DenialReason",h.denial_status "DenialStatus",h.appeal_filed "AppealFiled",h.divergence_alerts::text "DivergenceAlerts",h.due_date "DueDate"
from ged.hospital_billing_document h left join ged.document d on d.tenant_id=h.tenant_id and d.id=h.document_id {{where}} order by h.created_at desc limit 300
""", args, cancellationToken: ct))).AsList();
        var kpis = await connection.QuerySingleAsync<HospitalBillingKpis>(new CommandDefinition("""
select count(*)::int "Total",count(*) filter(where review_status='PENDING_REVIEW')::int "Pending",count(*) filter(where review_status='APPROVED')::int "Approved",count(*) filter(where review_status='DIVERGENT')::int "Divergent",count(*) filter(where denied_amount>0)::int "WithDenial",coalesce(sum(presented_amount),0) "Presented",coalesce(sum(approved_amount),0) "ApprovedAmount",coalesce(sum(denied_amount),0) "Denied",coalesce(sum(case when denial_status='IN_APPEAL' then denied_amount else 0 end),0) "InAppeal",coalesce(sum(recovered_amount),0) "Recovered",count(*) filter(where not has_ocr)::int "WithoutOcr",count(*) filter(where confidence<70)::int "LowConfidence" from ged.hospital_billing_document where tenant_id=@tenantId and reg_status='A'
""", new { tenantId }, cancellationToken: ct));
        return new(kpis, documents);
    }
    public async Task<HospitalBillingDocumentDto?> GetAsync(Guid tenantId, Guid id, CancellationToken ct) => (await DashboardAsync(tenantId, new(), ct)).Documents.FirstOrDefault(x => x.Id == id);
    public async Task<HospitalBillingDetails?> GetDetailsAsync(Guid tenantId, Guid id, CancellationToken ct)
    {
        var document = await GetAsync(tenantId, id, ct);
        if (document is null) return null;
        await using var connection = await db.OpenAsync(ct);
        var history = (await connection.QueryAsync<HospitalBillingReviewHistoryDto>(new CommandDefinition("""
select reviewed_at "ReviewedAt",review_status "Status",denial_status "DenialStatus",approved_amount "ApprovedAmount",denied_amount "DeniedAmount",recovered_amount "RecoveredAmount",notes "Notes"
from ged.hospital_billing_review_history where tenant_id=@tenantId and hospital_billing_id=@id order by reviewed_at desc limit 100
""", new { tenantId, id }, cancellationToken: ct))).AsList();
        return new(document, history);
    }
    public async Task<bool> ReviewAsync(Guid tenantId, Guid userId, HospitalBillingReviewRequest request, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var affected = await connection.ExecuteAsync(new CommandDefinition("""
update ged.hospital_billing_document set review_status=@Status,denial_status=nullif(@DenialStatus,''),approved_amount=@ApprovedAmount,denied_amount=@DeniedAmount,recovered_amount=@RecoveredAmount,reviewed_by=@userId,reviewed_at=now(),updated_at=now()
where tenant_id=@tenantId and id=@Id and reg_status='A' and @ApprovedAmount+@DeniedAmount<=presented_amount and @RecoveredAmount<=@DeniedAmount
""", new { tenantId, userId, request.Id, request.Status, request.DenialStatus, request.ApprovedAmount, request.DeniedAmount, request.RecoveredAmount }, transaction, cancellationToken: ct));
        if (affected == 0) { await transaction.RollbackAsync(ct); return false; }
        await connection.ExecuteAsync(new CommandDefinition("""
insert into ged.hospital_billing_review_history(tenant_id,hospital_billing_id,reviewed_by,review_status,denial_status,approved_amount,denied_amount,recovered_amount,notes)
values(@tenantId,@Id,@userId,@Status,nullif(@DenialStatus,''),@ApprovedAmount,@DeniedAmount,@RecoveredAmount,nullif(trim(@Notes),''))
""", new { tenantId, userId, request.Id, request.Status, request.DenialStatus, request.ApprovedAmount, request.DeniedAmount, request.RecoveredAmount, request.Notes }, transaction, cancellationToken: ct));
        await transaction.CommitAsync(ct);
        return true;
    }
    public async Task<HospitalBillingReports> ReportsAsync(Guid tenantId, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        const string select = "count(*)::int \"Documents\",coalesce(sum(presented_amount),0) \"Presented\",coalesce(sum(approved_amount),0) \"Approved\",coalesce(sum(denied_amount),0) \"Denied\",coalesce(sum(recovered_amount),0) \"Recovered\"";
        var insurer = (await connection.QueryAsync<HospitalBillingReportRow>(new CommandDefinition($"select coalesce(nullif(insurer,''),'Não identificado') \"Label\",{select} from ged.hospital_billing_document where tenant_id=@tenantId and reg_status='A' group by 1 order by \"Presented\" desc", new { tenantId }, cancellationToken: ct))).AsList();
        var competence = (await connection.QueryAsync<HospitalBillingReportRow>(new CommandDefinition($"select coalesce(nullif(competence,''),'Sem competência') \"Label\",{select} from ged.hospital_billing_document where tenant_id=@tenantId and reg_status='A' group by 1 order by \"Label\" desc", new { tenantId }, cancellationToken: ct))).AsList();
        var provider = (await connection.QueryAsync<HospitalBillingReportRow>(new CommandDefinition($"select coalesce(nullif(provider_name,''),'Prestador não identificado') \"Label\",{select} from ged.hospital_billing_document where tenant_id=@tenantId and reg_status='A' group by 1 order by \"Presented\" desc", new { tenantId }, cancellationToken: ct))).AsList();
        var reviewStatus = (await connection.QueryAsync<HospitalBillingReportRow>(new CommandDefinition($"select case review_status when 'PENDING_REVIEW' then 'Pendente de revisão' when 'APPROVED' then 'Aprovado' when 'DIVERGENT' then 'Divergente' else coalesce(nullif(review_status,''),'Sem status') end \"Label\",{select} from ged.hospital_billing_document where tenant_id=@tenantId and reg_status='A' group by 1 order by \"Presented\" desc", new { tenantId }, cancellationToken: ct))).AsList();
        var denials = (await connection.QueryAsync<HospitalBillingReportRow>(new CommandDefinition($"select coalesce(nullif(denial_reason,''),'Motivo não identificado') \"Label\",{select} from ged.hospital_billing_document where tenant_id=@tenantId and reg_status='A' and denied_amount>0 group by 1 order by \"Denied\" desc", new { tenantId }, cancellationToken: ct))).AsList();
        return new(insurer, competence, provider, reviewStatus, denials);
    }
}
