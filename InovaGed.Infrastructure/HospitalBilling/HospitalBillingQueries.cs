using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.HospitalBilling;

namespace InovaGed.Infrastructure.HospitalBilling;

public sealed class HospitalBillingQueries(IDbConnectionFactory db) : IHospitalBillingQueries
{
    public async Task<HospitalBillingDashboard> DashboardAsync(Guid tenantId, HospitalBillingFilter filter, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        const string where = """where h.tenant_id=@tenantId and h.reg_status='A' and (@Insurer is null or h.insurer ilike '%'||@Insurer||'%') and (@Competence is null or h.competence=@Competence) and (@Status is null or h.review_status=@Status) and (@HasDenial is null or (h.denied_amount>0)=@HasDenial)""";
        var args = new { tenantId, filter.Insurer, filter.Competence, filter.Status, filter.HasDenial };
        var documents = (await connection.QueryAsync<HospitalBillingDocumentDto>(new CommandDefinition($$"""
select h.id "Id",h.document_id "DocumentId",coalesce(d.title,d.code,'Documento hospitalar') "Title",h.document_type "DocumentType",h.insurer "Insurer",h.provider_name "Provider",h.guide_number "GuideNumber",h.authorization_number "AuthorizationNumber",h.competence "Competence",
case when nullif(h.patient_name,'') is null then 'Dado protegido' else left(h.patient_name,1)||repeat('*',greatest(length(h.patient_name)-2,3))||right(h.patient_name,1) end "MaskedPatient",
h.presented_amount "PresentedAmount",h.approved_amount "ApprovedAmount",h.denied_amount "DeniedAmount",h.recovered_amount "RecoveredAmount",h.confidence "Confidence",h.review_status "Status",h.denial_reason "DenialReason",h.due_date "DueDate"
from ged.hospital_billing_document h left join ged.document d on d.tenant_id=h.tenant_id and d.id=h.document_id {{where}} order by h.created_at desc limit 300
""", args, cancellationToken: ct))).AsList();
        var kpis = await connection.QuerySingleAsync<HospitalBillingKpis>(new CommandDefinition("""
select count(*)::int "Total",count(*) filter(where review_status='PENDING_REVIEW')::int "Pending",count(*) filter(where review_status='APPROVED')::int "Approved",count(*) filter(where review_status='DIVERGENT')::int "Divergent",count(*) filter(where denied_amount>0)::int "WithDenial",coalesce(sum(presented_amount),0) "Presented",coalesce(sum(approved_amount),0) "ApprovedAmount",coalesce(sum(denied_amount),0) "Denied",coalesce(sum(case when denial_status='IN_APPEAL' then denied_amount else 0 end),0) "InAppeal",coalesce(sum(recovered_amount),0) "Recovered",count(*) filter(where not has_ocr)::int "WithoutOcr",count(*) filter(where confidence<70)::int "LowConfidence" from ged.hospital_billing_document where tenant_id=@tenantId and reg_status='A'
""", new { tenantId }, cancellationToken: ct));
        return new(kpis, documents);
    }
    public async Task<HospitalBillingDocumentDto?> GetAsync(Guid tenantId, Guid id, CancellationToken ct) => (await DashboardAsync(tenantId, new(), ct)).Documents.FirstOrDefault(x => x.Id == id);
}
