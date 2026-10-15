using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Sectors;

namespace InovaGed.Infrastructure.Sectors;

/// <summary>Local evidence suggestions; does not invoke AI or confirm clinical execution.</summary>
public sealed class SectorVersionProcessor(IDbConnectionFactory db)
{
    public async Task ProcessAsync(GedProcessingJobDto job, CancellationToken ct)
    {
        if (!job.DocumentVersionId.HasValue) throw new InvalidOperationException("A versão do processamento não foi informada.");
        await using var conn = await db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
update ged.sector_document_link set extraction_status='PROCESSANDO'
where tenant_id=@TenantId and version_id=@DocumentVersionId and reg_status='A'
 and exists(select 1 from ged.processing_job where tenant_id=@TenantId and id=@Id and status='PROCESSING');
""",job,cancellationToken:ct));
        await using var tx = await conn.BeginTransactionAsync(ct);
        var previous = await conn.QuerySingleOrDefaultAsync<State>(new CommandDefinition("""
select input_hash as InputHash from ged.sector_processing_state
where tenant_id=@TenantId and version_id=@DocumentVersionId for update
""", job, tx, cancellationToken: ct));
        if (previous is null) throw new InvalidOperationException("O contexto durável do processamento não foi encontrado.");
        var rows = (await conn.QueryAsync<Input>(new CommandDefinition("""
select l.id as LinkId,l.document_id as DocumentId,l.encounter_id as EncounterId,l.catalog_code as Catalog,
 coalesce(nullif(btrim(v.ocr_text),''),nullif(btrim(s.ocr_text),''),nullif(btrim(v.content_text),'')) as Text,
 case when nullif(btrim(v.ocr_text),'') is not null or nullif(btrim(s.ocr_text),'') is not null then 'OCR' else 'TEXTO_NATIVO' end as Source,
 oj.status::text as OcrStatus,ps.status::text as PreviewStatus,l.ocr_status as LinkOcr
from ged.sector_document_link l
join ged.document_version v on v.tenant_id=l.tenant_id and v.id=l.version_id
join ged.document d on d.tenant_id=l.tenant_id and d.id=l.document_id and (d.reg_status='A' or d.reg_status is null)
left join ged.document_search s on s.tenant_id=v.tenant_id and s.version_id=v.id
left join ged.preview_status ps on ps.tenant_id=v.tenant_id and ps.document_version_id=v.id
left join lateral(select status from ged.ocr_job where tenant_id=v.tenant_id and document_version_id=v.id order by requested_at desc limit 1) oj on true
where l.tenant_id=@TenantId and l.version_id=@DocumentVersionId and l.reg_status='A'
order by l.id
""", job, tx, cancellationToken: ct))).ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            rows.Select(x => new { x.LinkId,x.EncounterId,x.Catalog,x.Text,x.Source,Mechanism="REGRAS_LOCAIS_V1" })))));
        foreach (var row in rows)
        {
            var extraction = row.Text is not null ? "CONCLUIDO" : row.OcrStatus is "FAILED" or "ERROR" ? "BLOQUEADO_OCR"
                : row.OcrStatus == "COMPLETED" || row.LinkOcr == "NAO_APLICAVEL" ? "SEM_TEXTO" : "AGUARDANDO_TEXTO";
            var ocr = row.OcrStatus switch { "COMPLETED" => "CONCLUIDO", "FAILED" or "ERROR" => "FALHOU", "PROCESSING" => "PROCESSANDO", "PENDING" => "ENFILEIRADO", "CANCELLED" or "CANCELED" => "CANCELADO", _ => row.LinkOcr };
            var preview = row.PreviewStatus switch { "READY" => "CONCLUIDO", "FAILED" or "ERROR" => "FALHOU", "RUNNING" or "PROCESSING" => "PROCESSANDO", "PENDING" or "QUEUED" => "ENFILEIRADO", "UNSUPPORTED" => "NAO_APLICAVEL", "CANCELLED" or "CANCELED" => "CANCELADO", _ => (string?)null };
            if (hash != previous.InputHash)
            {
                var clinicalHash=SectorClinicalEvidence.Fingerprint(row.LinkId,row.DocumentId,job.DocumentVersionId.Value,row.EncounterId,row.Catalog,row.Source,row.Text);
                await conn.ExecuteAsync(new CommandDefinition("""
update ged.sector_extracted_field f set status='SUPERADO',updated_at=now()
where f.tenant_id=@TenantId and f.link_id=@LinkId and f.version_id=@DocumentVersionId
 and f.mechanism='IA_CLINICA_V1' and f.status='SUGERIDO' and f.source_fingerprint is distinct from @clinicalHash
 and not exists(select 1 from ged.sector_field_review r where r.tenant_id=f.tenant_id and r.field_id=f.id);
""",new{job.TenantId,job.DocumentVersionId,row.LinkId,clinicalHash},tx,cancellationToken:ct));
                // Preserve every human decision, including rejected/inconclusive evidence.
                // Rows referenced by historical review records must also remain intact.
                await conn.ExecuteAsync(new CommandDefinition("""
delete from ged.sector_extracted_field f where tenant_id=@TenantId and link_id=@LinkId and version_id=@DocumentVersionId
 and status='SUGERIDO' and mechanism in ('REGRAS_LOCAIS','REGRAS_LOCAIS_V1')
 and not exists(select 1 from ged.sector_field_review r where r.tenant_id=f.tenant_id and r.field_id=f.id);
update ged.sector_finding set review_status='SUPERADO',updated_at=now()
where tenant_id=@TenantId and link_id=@LinkId and category='INFORMACAO_LOCALIZADA'
 and review_status='ABERTO' and actor_id is null;
""", new { job.TenantId,job.DocumentVersionId,row.LinkId }, tx, cancellationToken: ct));
                foreach (var fact in SectorExtraction.Read(row.Text,row.Catalog))
                {
                    await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.sector_extracted_field
 (id,tenant_id,link_id,version_id,field_key,context_code,suggested_value,evidence_excerpt,page_number,status,source_kind,mechanism)
select gen_random_uuid(),@TenantId,@LinkId,@DocumentVersionId,@Category,@Context,@Excerpt,@Excerpt,@Page,'SUGERIDO',@Source,'REGRAS_LOCAIS_V1'
where not exists(select 1 from ged.sector_extracted_field where tenant_id=@TenantId and link_id=@LinkId
 and version_id=@DocumentVersionId and field_key=@Category and evidence_excerpt=@Excerpt);
insert into ged.sector_finding(id,tenant_id,encounter_id,link_id,description,category,evidence_excerpt,page_number)
select gen_random_uuid(),@TenantId,@EncounterId,@LinkId,@description,'INFORMACAO_LOCALIZADA',@Excerpt,@Page
where not exists(select 1 from ged.sector_finding where tenant_id=@TenantId and link_id=@LinkId and evidence_excerpt=@Excerpt);
update ged.sector_finding set review_status='ABERTO',updated_at=now()
where tenant_id=@TenantId and link_id=@LinkId and evidence_excerpt=@Excerpt and review_status='SUPERADO' and actor_id is null;
""", new { job.TenantId,job.DocumentVersionId,row.LinkId,row.EncounterId,row.Source,fact.Category,fact.Context,fact.Excerpt,fact.Page,description=SectorFindings.Describe(fact) }, tx, cancellationToken: ct));
                }
            }
            await conn.ExecuteAsync(new CommandDefinition("""
update ged.sector_document_link set extraction_status=@extraction,ocr_status=@ocr,preview_status=coalesce(@preview,preview_status)
where tenant_id=@TenantId and id=@LinkId
""", new { job.TenantId,row.LinkId,extraction,ocr,preview }, tx, cancellationToken: ct));
        }
        // Queue completion shares the notification lock and commit: a concurrent text
        // update can enqueue its next job after this commit, never lose its notification.
        await conn.ExecuteAsync(new CommandDefinition("""
update ged.sector_processing_state set input_hash=@hash,completed_at=clock_timestamp() where tenant_id=@TenantId and version_id=@DocumentVersionId;
update ged.processing_job set status='COMPLETED',finished_at=now(),locked_by=null,locked_at=null,updated_at=now()
where tenant_id=@TenantId and id=@Id and job_type='SECTOR_EXTRACT';
""", new { job.TenantId,job.DocumentVersionId,job.Id,hash }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }

    public async Task RefreshQueueStatusAsync(GedProcessingJobDto job,CancellationToken ct)
    {
        await using var conn=await db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
update ged.sector_document_link l set extraction_status=case when j.status='FAILED' then 'FALHOU' else 'ENFILEIRADO' end
from ged.processing_job j where j.tenant_id=@TenantId and j.id=@Id and j.status in ('FAILED','PENDING')
 and l.tenant_id=j.tenant_id and l.version_id=j.document_version_id and l.reg_status='A';
""",job,cancellationToken:ct));
    }

    private sealed class State { public string? InputHash { get; set; } }
    private sealed class Input
    {
        public Guid LinkId { get; set; }
        public Guid DocumentId { get; set; }
        public Guid EncounterId { get; set; }
        public string Catalog { get; set; } = "";
        public string? Text { get; set; }
        public string Source { get; set; } = "";
        public string? OcrStatus { get; set; }
        public string? PreviewStatus { get; set; }
        public string LinkOcr { get; set; } = "";
    }
}
