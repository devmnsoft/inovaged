using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.HospitalBilling;

namespace InovaGed.Infrastructure.HospitalBilling;

public sealed class HospitalBillingQueries(IDbConnectionFactory db) : IHospitalBillingQueries
{
    public async Task<HospitalBillingDashboard> DashboardAsync(Guid tenantId, HospitalBillingFilter filter, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        const string where = """where h.tenant_id=@tenantId and h.reg_status='A'
and (@Insurer is null or h.insurer ilike '%'||@Insurer||'%') and (@Competence is null or h.competence=@Competence)
and (@Status is null or h.review_status=@Status) and (@HasDenial is null or (h.denied_amount>0)=@HasDenial)
and (@Unit is null or h.provider_name ilike '%'||@Unit||'%' or h.cnes ilike '%'||@Unit||'%')
and (@Patient is null or h.patient_name ilike '%'||@Patient||'%') and (@DocumentType is null or h.document_type=@DocumentType)
and (@MinimumAmount is null or h.presented_amount>=@MinimumAmount) and (@MaximumAmount is null or h.presented_amount<=@MaximumAmount)
and (@OcrPending is null or (not h.has_ocr)=@OcrPending)
and (@HasDivergence is null or (h.review_status='DIVERGENT' or h.divergence_alerts<>'[]'::jsonb)=@HasDivergence)
and (@Term is null or concat_ws(' ',h.insurer,h.provider_name,h.provider_cnpj,h.cnes,h.guide_number,h.authorization_number,h.batch_number,h.invoice_number,h.procedure_name,h.procedure_code,h.denial_reason,h.document_type) ilike '%'||@Term||'%')""";
        var args = new { tenantId, filter.Insurer, filter.Competence, filter.Status, filter.HasDenial, filter.Term, filter.Unit, filter.Patient, filter.DocumentType, filter.MinimumAmount, filter.MaximumAmount, filter.OcrPending, filter.HasDivergence };
        var documents = (await connection.QueryAsync<HospitalBillingDocumentDto>(new CommandDefinition($$"""
select h.id "Id",h.document_id "DocumentId",coalesce(d.title,d.code,'Documento hospitalar') "Title",h.document_type "DocumentType",h.insurer "Insurer",h.provider_name "Provider",h.provider_cnpj "ProviderCnpj",h.cnes "Cnes",h.guide_number "GuideNumber",h.authorization_number "AuthorizationNumber",h.batch_number "BatchNumber",h.invoice_number "InvoiceNumber",h.competence "Competence",h.procedure_name "ProcedureName",h.procedure_code "ProcedureCode",
case when nullif(h.patient_name,'') is null then 'Dado protegido' else left(h.patient_name,1)||repeat('*',greatest(length(h.patient_name)-2,3))||right(h.patient_name,1) end "MaskedPatient",
h.presented_amount "PresentedAmount",h.approved_amount "ApprovedAmount",h.denied_amount "DeniedAmount",h.recovered_amount "RecoveredAmount",h.confidence "Confidence",h.review_status "Status",h.denial_reason "DenialReason",h.denial_status "DenialStatus",h.appeal_filed "AppealFiled",h.has_ocr "HasOcr",h.divergence_alerts::text "DivergenceAlerts",h.due_date "DueDate"
from ged.hospital_billing_document h left join ged.document d on d.tenant_id=h.tenant_id and d.id=h.document_id {{where}} order by h.created_at desc limit 300
""", args, cancellationToken: ct))).AsList();
        var kpis = await connection.QuerySingleAsync<HospitalBillingKpis>(new CommandDefinition("""
select count(*)::int "Total",count(*) filter(where h.review_status='PENDING_REVIEW')::int "Pending",count(*) filter(where h.review_status='APPROVED')::int "Approved",count(*) filter(where h.review_status='DIVERGENT')::int "Divergent",count(*) filter(where h.denied_amount>0)::int "WithDenial",coalesce(sum(h.presented_amount),0) "Presented",coalesce(sum(h.approved_amount),0) "ApprovedAmount",coalesce(sum(h.denied_amount),0) "Denied",coalesce(sum(case when h.denial_status='IN_APPEAL' then h.denied_amount else 0 end),0) "InAppeal",coalesce(sum(h.recovered_amount),0) "Recovered",count(*) filter(where not h.has_ocr)::int "WithoutOcr",count(*) filter(where h.confidence<70)::int "LowConfidence" from ged.hospital_billing_document h """ + where, args, cancellationToken: ct));
        return new(kpis, documents);
    }
    public async Task<HospitalBillingDocumentDto?> GetAsync(Guid tenantId, Guid id, CancellationToken ct) => (await DashboardAsync(tenantId, new(), ct)).Documents.FirstOrDefault(x => x.Id == id);
    public async Task<HospitalBillingDetails?> GetDetailsAsync(Guid tenantId, Guid id, CancellationToken ct)
    {
        var document = await GetAsync(tenantId, id, ct);
        if (document is null) return null;
        await using var connection = await db.OpenAsync(ct);
        var history = (await connection.QueryAsync<HospitalBillingReviewHistoryDto>(new CommandDefinition("""
select reviewed_at "ReviewedAt",reviewed_by "ReviewedBy",coalesce(previous_review_status,'PENDING_REVIEW') "PreviousStatus",review_status "Status",
previous_denial_status "PreviousDenialStatus",denial_status "DenialStatus",approved_amount "ApprovedAmount",denied_amount "DeniedAmount",recovered_amount "RecoveredAmount",notes "Notes",changed_fields::text "ChangedFields"
from ged.hospital_billing_review_history where tenant_id=@tenantId and hospital_billing_id=@id order by reviewed_at desc limit 100
""", new { tenantId, id }, cancellationToken: ct))).AsList();
        return new(document, history);
    }
    public async Task<bool> ReviewAsync(Guid tenantId, Guid userId, HospitalBillingReviewRequest request, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var affected = await connection.ExecuteAsync(new CommandDefinition("""
with previous as (
 select * from ged.hospital_billing_document where tenant_id=@tenantId and id=@Id and reg_status='A'
 and @ApprovedAmount+@DeniedAmount<=presented_amount and @RecoveredAmount<=@DeniedAmount for update
), history as (
 insert into ged.hospital_billing_review_history(tenant_id,hospital_billing_id,reviewed_by,previous_review_status,review_status,previous_denial_status,denial_status,approved_amount,denied_amount,recovered_amount,notes,changed_fields)
 select @tenantId,id,@userId,review_status,@Status,denial_status,nullif(@DenialStatus,''),@ApprovedAmount,@DeniedAmount,@RecoveredAmount,nullif(trim(@Notes),''),
 jsonb_strip_nulls(jsonb_build_object(
  'status',case when review_status is distinct from @Status then jsonb_build_object('from',review_status,'to',@Status) end,
  'denialStatus',case when denial_status is distinct from nullif(@DenialStatus,'') then jsonb_build_object('from',denial_status,'to',nullif(@DenialStatus,'')) end,
  'approvedAmount',case when approved_amount is distinct from @ApprovedAmount then jsonb_build_object('from',approved_amount,'to',@ApprovedAmount) end,
  'deniedAmount',case when denied_amount is distinct from @DeniedAmount then jsonb_build_object('from',denied_amount,'to',@DeniedAmount) end,
  'recoveredAmount',case when recovered_amount is distinct from @RecoveredAmount then jsonb_build_object('from',recovered_amount,'to',@RecoveredAmount) end,
  'denialReason',case when denial_reason is distinct from nullif(trim(@DenialReason),'') then jsonb_build_object('from',denial_reason,'to',nullif(trim(@DenialReason),'')) end,
  'appealDueDate',case when due_date is distinct from @AppealDueDate then jsonb_build_object('from',due_date,'to',@AppealDueDate) end))
 from previous returning hospital_billing_id
)
update ged.hospital_billing_document h set review_status=@Status,denial_status=nullif(@DenialStatus,''),denial_reason=nullif(trim(@DenialReason),''),due_date=@AppealDueDate,
 approved_amount=@ApprovedAmount,denied_amount=@DeniedAmount,recovered_amount=@RecoveredAmount,appeal_filed=(@DenialStatus='IN_APPEAL'),reviewed_by=@userId,reviewed_at=now(),updated_at=now()
from history where h.tenant_id=@tenantId and h.id=history.hospital_billing_id
""", new { tenantId, userId, request.Id, request.Status, request.DenialStatus, request.DenialReason, request.AppealDueDate, request.ApprovedAmount, request.DeniedAmount, request.RecoveredAmount, request.Notes }, transaction, cancellationToken: ct));
        if (affected == 0) { await transaction.RollbackAsync(ct); return false; }
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
        var reviewStatus = (await connection.QueryAsync<HospitalBillingReportRow>(new CommandDefinition($"select case review_status when 'PENDING_REVIEW' then 'Pendente de revisão' when 'APPROVED' then 'Aprovado' when 'DIVERGENT' then 'Divergente' when 'DENIED' then 'Glosado' when 'APPEAL_IN_REVIEW' then 'Recurso em análise' when 'RECOVERED' then 'Recuperado' when 'CLOSED' then 'Encerrado' else coalesce(nullif(review_status,''),'Sem status') end \"Label\",{select} from ged.hospital_billing_document where tenant_id=@tenantId and reg_status='A' group by 1 order by \"Presented\" desc", new { tenantId }, cancellationToken: ct))).AsList();
        var denials = (await connection.QueryAsync<HospitalBillingReportRow>(new CommandDefinition($"select coalesce(nullif(denial_reason,''),'Motivo não identificado') \"Label\",{select} from ged.hospital_billing_document where tenant_id=@tenantId and reg_status='A' and denied_amount>0 group by 1 order by \"Denied\" desc", new { tenantId }, cancellationToken: ct))).AsList();
        return new(insurer, competence, provider, reviewStatus, denials);
    }
}
