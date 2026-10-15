using Dapper;
using InovaGed.Application.Ged.Documents;
using InovaGed.Infrastructure.Ged.Documents;
using InovaGed.Infrastructure.Sectors;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace InovaGed.Application.Tests.Sectors;

public sealed partial class SectorEntryAndOpeningGateTests
{
    private static async Task VerifyVersionProcessingAsync(NpgsqlConnection db, Factory factory, Guid tenant, Guid user, Guid patient, Guid encounter)
    {
        var document = Guid.NewGuid(); var version = Guid.NewGuid(); var link = Guid.NewGuid();
        var args = new { tenant,user,patient,encounter,document,version,link };
        var processor = new SectorVersionProcessor(factory);
        var queue = new GedProcessingJobRepository(factory,NullLogger<GedProcessingJobRepository>.Instance);
        await db.ExecuteAsync("""
insert into ged.document(id,tenant_id,code,title,is_confidential,created_by)
values(@document,@tenant,'PROCESSING-FIXTURE','Documento sintético',false,@user);
insert into ged.document_version(id,tenant_id,document_id,version_number,file_name,file_extension,file_size_bytes,storage_path,checksum_sha256,content_type,created_by)
values(@version,@tenant,@document,1,'synthetic.pdf','.pdf',100,'synthetic.pdf',repeat('a',64),'application/pdf',@user);
insert into ged.sector_document_link(id,tenant_id,patient_id,encounter_id,document_id,version_id,catalog_code,origin,original_file_name,content_sha256,uploaded_by,transfer_status,preview_status,ocr_status,extraction_status)
values(@link,@tenant,@patient,@encounter,@document,@version,'DS23','GED_UPLOAD','synthetic.pdf',repeat('a',64),@user,'GRAVADO','AGUARDANDO','ENFILEIRADO','AGUARDANDO');
""",args);
        await Drain();
        Assert.Equal("AGUARDANDO_TEXTO",await db.ExecuteScalarAsync<string>("select extraction_status from ged.sector_document_link where id=@link",args));
        Assert.Equal(0,await db.ExecuteScalarAsync<int>("select count(*) from ged.sector_extracted_field where link_id=@link",args));

        await db.ExecuteAsync("update ged.document_version set content_text='--- página 1 ---' || chr(10) || 'Medicamento sintético 10 mg via oral' where id=@version",args);
        await Drain();
        var field = await db.ExecuteScalarAsync<Guid>("select id from ged.sector_extracted_field where link_id=@link",args);
        Assert.Equal("TEXTO_NATIVO",await db.ExecuteScalarAsync<string>("select source_kind from ged.sector_extracted_field where id=@field",new { field }));
        Assert.Equal(1,await db.ExecuteScalarAsync<int>("select page_number from ged.sector_extracted_field where id=@field",new { field }));
        Assert.Equal("PRESCRITO",await db.ExecuteScalarAsync<string>("select context_code from ged.sector_extracted_field where id=@field",new { field }));
        // Repeated completion/notification cannot duplicate suggestions or findings.
        await db.ExecuteAsync("select ged.sector_request_extraction(@tenant,@version); select ged.sector_request_extraction(@tenant,@version)",args);
        var repeated = Assert.Single(await queue.DequeueAsync("sector-test",5,CancellationToken.None));
        await processor.ProcessAsync(repeated,CancellationToken.None);
        await processor.ProcessAsync(repeated,CancellationToken.None);
        Assert.Equal(field,await db.ExecuteScalarAsync<Guid>("select id from ged.sector_extracted_field where link_id=@link",args));

        // A reviewed value and its history survive a changed OCR result.
        await db.ExecuteAsync("""
update ged.sector_extracted_field set status='CORRIGIDO',confirmed_value='Valor humano sintético',updated_by=@user where id=@field;
insert into ged.sector_field_review(id,tenant_id,field_id,to_status,to_value,actor_id)
values(gen_random_uuid(),@tenant,@field,'CORRIGIDO','Valor humano sintético',@user);
update ged.document_version set ocr_text='--- página 2 ---' || chr(10) || 'Outro medicamento sintético 20 mg via oral' where id=@version;
""",new { tenant,user,field,version });
        await Drain();
        Assert.Equal("Valor humano sintético",await db.ExecuteScalarAsync<string>("select confirmed_value from ged.sector_extracted_field where id=@field",new { field }));
        Assert.Equal(1,await db.ExecuteScalarAsync<int>("select count(*) from ged.sector_extracted_field where link_id=@link and source_kind='OCR' and page_number=2 and mechanism='REGRAS_LOCAIS_V1'",args));

        // Crash after dequeue: the canonical lease recovery returns the job to the queue.
        await db.ExecuteAsync("select ged.sector_request_extraction(@tenant,@version)",args);
        var abandoned=Assert.Single(await queue.DequeueAsync("crashed-worker",5,CancellationToken.None));
        await db.ExecuteAsync("update ged.processing_job set locked_at=now()-interval '20 minutes' where id=@id",new { id=abandoned.Id });
        Assert.Equal(1,await queue.RecoverAbandonedAsync("SECTOR_EXTRACT",TimeSpan.FromMinutes(10),CancellationToken.None));
        await Drain();

        // A notification arriving while extraction holds the state lock is not lost.
        var suggested = await db.ExecuteScalarAsync<Guid>("select id from ged.sector_extracted_field where link_id=@link and status='SUGERIDO'",args);
        await db.ExecuteAsync("update ged.document_version set ocr_text='Medicamento sintético 30 mg via oral' where id=@version",args);
        var racing=Assert.Single(await queue.DequeueAsync("sector-test",5,CancellationToken.None));
        await using (var blocker = await factory.OpenAsync(CancellationToken.None))
        {
            await using var tx=await blocker.BeginTransactionAsync();
            await blocker.ExecuteAsync("select id from ged.sector_extracted_field where id=@suggested for update",new { suggested },tx);
            var processing=processor.ProcessAsync(racing,CancellationToken.None);
            await WaitForLock("delete from ged.sector_extracted_field");
            await using var updater=await factory.OpenAsync(CancellationToken.None);
            var update=updater.ExecuteAsync("update ged.document_version set ocr_text='Medicamento sintético 40 mg via oral' where id=@version",args);
            await WaitForLock("update ged.document_version");
            await tx.CommitAsync();
            await Task.WhenAll(processing,update);
        }
        await Drain();
        Assert.Equal(1,await db.ExecuteScalarAsync<int>("select count(*) from ged.sector_extracted_field where link_id=@link and status='SUGERIDO' and evidence_excerpt like '%40 mg%'",args));
        Assert.Equal(1,await db.ExecuteScalarAsync<int>("select count(*) from ged.sector_field_review where field_id=@field",new { field }));
        // Failure between suggestion replacement and queue completion rolls everything back.
        await db.ExecuteAsync("""
update ged.document_version set ocr_text='Medicamento sintético 50 mg via oral' where id=@version;
create function ged.sector_test_fault() returns trigger language plpgsql as $$ begin raise exception 'synthetic processing failure'; end $$;
create trigger sector_test_fault before insert on ged.sector_extracted_field for each row execute function ged.sector_test_fault();
""",args);
        var failing=Assert.Single(await queue.DequeueAsync("sector-test",5,CancellationToken.None));
        await Assert.ThrowsAsync<PostgresException>(()=>processor.ProcessAsync(failing,CancellationToken.None));
        Assert.Equal(1,await db.ExecuteScalarAsync<int>("select count(*) from ged.sector_extracted_field where link_id=@link and status='SUGERIDO' and evidence_excerpt like '%40 mg%'",args));
        await queue.FailAsync(tenant,failing.Id,"Synthetic failure",TimeSpan.FromSeconds(5),CancellationToken.None);
        await processor.RefreshQueueStatusAsync(failing,CancellationToken.None);
        Assert.Equal("ENFILEIRADO",await db.ExecuteScalarAsync<string>("select extraction_status from ged.sector_document_link where id=@link",args));
        await db.ExecuteAsync("""
drop trigger sector_test_fault on ged.sector_extracted_field;
drop function ged.sector_test_fault();
update ged.processing_job set next_attempt_at=now() where id=@id;
""",new { id=failing.Id });
        await Drain();
        Assert.Equal(1,await db.ExecuteScalarAsync<int>("select count(*) from ged.sector_extracted_field where link_id=@link and status='SUGERIDO' and evidence_excerpt like '%50 mg%'",args));
        await db.ExecuteAsync("update ged.document_version set ocr_text=null,content_text=null where id=@version",args);
        var ocr=new InovaGed.Infrastructure.Ocr.OcrJobRepository(factory,NullLogger<InovaGed.Infrastructure.Ocr.OcrJobRepository>.Instance);
        var ocrId=await ocr.EnqueueAsync(tenant,version,user,false,CancellationToken.None);
        await db.ExecuteAsync("update ged.ocr_job set status='ERROR' where id=@ocrId",new{ocrId});
        await Drain();
        Assert.Equal("BLOQUEADO_OCR",await db.ExecuteScalarAsync<string>("select extraction_status from ged.sector_document_link where id=@link",args));
        Assert.Equal("FALHOU",await db.ExecuteScalarAsync<string>("select ocr_status from ged.sector_document_link where id=@link",args));
        await db.ExecuteAsync("update ged.sector_document_link set reg_status='I' where id=@link",args);

        async Task Drain()
        {
            var jobs=await queue.DequeueAsync("sector-test",5,CancellationToken.None);
            Assert.NotEmpty(jobs);
            foreach(var job in jobs)
            {
                Assert.Equal("SECTOR_EXTRACT",job.JobType);
                await processor.ProcessAsync(job,CancellationToken.None);
            }
            Assert.Empty(await queue.DequeueAsync("sector-test",5,CancellationToken.None));
        }
        async Task WaitForLock(string fragment)
        {
            for(var attempt=0;attempt<200;attempt++)
            {
                if(await db.ExecuteScalarAsync<bool>("select exists(select 1 from pg_stat_activity where datname=current_database() and wait_event_type='Lock' and query like @pattern)",new { pattern="%"+fragment+"%" })) return;
                await Task.Delay(25);
            }
            throw new TimeoutException("O processador não alcançou a barreira de concorrência.");
        }
    }
}
