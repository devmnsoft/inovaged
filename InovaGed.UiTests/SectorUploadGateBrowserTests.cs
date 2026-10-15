using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace InovaGed.UiTests;

[Collection("Sector external fixture")]
public sealed class SectorUploadGateBrowserTests
{
    [SectorPartnerGateFact]
    public async Task Interrupted_parts_context_binding_stored_file_link_failure_and_concurrent_completion()
    {
        const string tenant="50000000-0000-0000-0000-000000000001";
        const string patient="50000000-0000-0000-0000-000000000004";
        const string encounter="50000000-0000-0000-0000-000000000005";
        var app=Environment.GetEnvironmentVariable("INOVAGED_SECTOR_APP") ?? "https://localhost:7189";
        Assert.StartsWith("https://localhost:",app);
        await Sql("update ged.sector_document_link set reg_status='I' where tenant_id='50000000-0000-0000-0000-000000000001' and original_file_name like 'SECTOR-UPLOAD-GATE-%';");
        using var playwright=await Playwright.CreateAsync();
        await using var browser=await playwright.Chromium.LaunchAsync(new() { Headless=true,Channel="chromium" });
        await using var user=await browser.NewContextAsync(new() { IgnoreHTTPSErrors=true });
        var page=await user.NewPageAsync();
        await page.GotoAsync(app+"/DemaisSetores/Cadastrar");
        await page.FillAsync("[name='Email']","sector@example.test");
        await page.FillAsync("[name='Password']","Sector-Test-2026!");
        await page.Locator("#loginForm button[type='submit']").ClickAsync();
        await page.Locator("[data-ds-register]").WaitForAsync();
        var token=await page.Locator("[data-ds-register] input[name='__RequestVerificationToken']").First.InputValueAsync();
        var filename="SECTOR-UPLOAD-GATE-"+Guid.NewGuid().ToString("N")+".txt";
        var data=new byte[9*1024*1024+177];
        Array.Fill(data,(byte)'A');
        System.Text.Encoding.UTF8.GetBytes(filename+"\nSynthetic upload only\n").CopyTo(data,0);
        var started=await Post("IniciarEnvio",("fileName",filename),("contentType","text/plain"),("totalBytes",data.Length.ToString()));
        Assert.True(started.TryGetProperty("uploadId",out var uploadValue),started.ToString());
        var upload=uploadValue.GetGuid(); var chunk=started.GetProperty("chunkSize").GetInt32();
        var total=started.GetProperty("totalChunks").GetInt32();
        Assert.True(total>=2);
        var missing=await Complete();
        Assert.False(missing.GetProperty("stored").GetBoolean());
        Assert.Equal("TRANSFERENCIA",missing.GetProperty("stage").GetString());
        // A malformed part must not become accepted or change the received count.
        var bad=await Part(0,new byte[1]);
        Assert.False(bad.TryGetProperty("received",out _));
        var first=await Part(0,data[..chunk]);
        Assert.Equal(1,first.GetProperty("received").GetInt32());
        Assert.Equal(1,(await Part(0,data[..chunk])).GetProperty("received").GetInt32());
        var status=await Status();
        Assert.Equal(total-1,status.GetProperty("missing").GetArrayLength());
        // Switching the encounter/type cannot persist an already-started upload.
        var wrong=await Post("ConcluirEnvio",("uploadId",upload.ToString()),("encounterId",Guid.NewGuid().ToString()),("catalogCode","DS08"));
        Assert.Equal("CONTEXTO",wrong.GetProperty("stage").GetString());
        for(var i=1;i<total;i++)
            Assert.Equal(i+1,(await Part(i,data[(i*chunk)..Math.Min(data.Length,(i+1)*chunk)])).GetProperty("received").GetInt32());
        wrong=await Post("ConcluirEnvio",("uploadId",upload.ToString()),("catalogCode","DS08"));
        Assert.Equal("CONTEXTO",wrong.GetProperty("stage").GetString());
        Assert.False((await Status()).GetProperty("stored").GetBoolean());
        await Sql("""
create or replace function ged.sector_upload_gate_fault() returns trigger language plpgsql as $$
begin
 if NEW.tenant_id='50000000-0000-0000-0000-000000000001' and NEW.original_file_name like 'SECTOR-UPLOAD-GATE-%' then
  raise exception 'Synthetic link failure';
 end if;
 return NEW;
end $$;
drop trigger if exists sector_upload_gate_fault on ged.sector_document_link;
create trigger sector_upload_gate_fault before insert on ged.sector_document_link for each row execute function ged.sector_upload_gate_fault();
create or replace function ged.sector_upload_receipt_gate_fault() returns trigger language plpgsql as $$
begin
 if NEW.tenant_id='50000000-0000-0000-0000-000000000001' and NEW.original_file_name like 'SECTOR-UPLOAD-GATE-%' and NEW.status='COMPLETED' then
  raise exception 'Synthetic failure after canonical receipt commit';
 end if;
 return NEW;
end $$;
drop trigger if exists sector_upload_receipt_gate_fault on ged.upload_session;
create trigger sector_upload_receipt_gate_fault before update of status on ged.upload_session for each row execute function ged.sector_upload_receipt_gate_fault();
""");
        Guid document,version;
        try
        {
            var failed=await Request("ConcluirEnvio",("uploadId",upload.ToString()),("confirmAnotherOccurrence","true"));
            Assert.Equal(500,failed.Status);
            status=await Status();
            Assert.Equal("COMPLETING",status.GetProperty("status").GetString());
            document=status.GetProperty("documentId").GetGuid(); version=status.GetProperty("versionId").GetGuid();
            Assert.Equal("1",(await Sql($"select count(*) from ged.document_version where tenant_id='{tenant}' and document_id='{document:D}';")).Trim());
            Assert.False((await Post("CancelarEnvio",("uploadId",upload.ToString()))).GetProperty("cancelled").GetBoolean());
            await Sql("drop trigger if exists sector_upload_receipt_gate_fault on ged.upload_session; drop function if exists ged.sector_upload_receipt_gate_fault();");
            // A later version must not replace the exact version named by the durable receipt.
            var later=Guid.NewGuid();
            await Sql($"""
insert into ged.document_version(id,tenant_id,document_id,version_number,file_name,file_extension,file_size_bytes,storage_path,checksum_sha256,content_type,created_by)
select '{later:D}',tenant_id,document_id,version_number+1,'synthetic-later.txt',file_extension,file_size_bytes,storage_path,checksum_sha256,content_type,created_by
from ged.document_version where tenant_id='{tenant}' and id='{version:D}';
update ged.document set current_version_id='{later:D}' where tenant_id='{tenant}' and id='{document:D}';
""");
            failed=await Request("ConcluirEnvio",("uploadId",upload.ToString()),("confirmAnotherOccurrence","true"));
            Assert.Equal(500,failed.Status); // Receipt recovered; separate link failure remains injected.
            status=await Status();Assert.True(status.GetProperty("stored").GetBoolean());
            Assert.Equal(version,status.GetProperty("versionId").GetGuid());
            var count=await Sql($"select count(*) from ged.sector_document_link where tenant_id='{tenant}' and version_id='{version:D}';");
            Assert.Equal("0",count.Trim());
        }
        finally
        {
            await Sql("drop trigger if exists sector_upload_gate_fault on ged.sector_document_link; drop function if exists ged.sector_upload_gate_fault(); drop trigger if exists sector_upload_receipt_gate_fault on ged.upload_session; drop function if exists ged.sector_upload_receipt_gate_fault();");
        }
        // Both replays reuse the persisted document/version and produce exactly one link.
        var completions=await Task.WhenAll(Complete(),Complete());
        foreach(var completed in completions)
        {
            Assert.True(completed.GetProperty("stored").GetBoolean(),completed.ToString());
            Assert.Equal(document,completed.GetProperty("documentId").GetGuid());
            Assert.Equal(version,completed.GetProperty("versionId").GetGuid());
        }
        Assert.Equal("1",(await Sql($"select count(*) from ged.sector_document_link where tenant_id='{tenant}' and version_id='{version:D}';")).Trim());
        Assert.Equal("2",(await Sql($"select count(*) from ged.document_version where tenant_id='{tenant}' and document_id='{document:D}';")).Trim()); // Original plus the explicitly inserted later version, no replay version.
        Assert.Equal("1",(await Sql($"select count(*) from ged.processing_job where tenant_id='{tenant}' and document_version_id='{version:D}' and job_type='PREVIEW';")).Trim());
        var file=await user.APIRequest.GetAsync(app+"/DemaisSetores/Arquivo?versionId="+version);
        Assert.Equal(200,file.Status);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(data)),Convert.ToHexString(SHA256.HashData(await file.BodyAsync())));
        Assert.False((await Post("CancelarEnvio",("uploadId",upload.ToString()))).GetProperty("cancelled").GetBoolean());
        Assert.True((await Status()).GetProperty("stored").GetBoolean());
        wrong=await Post("ConcluirEnvio",("uploadId",upload.ToString()),("patientId",Guid.NewGuid().ToString()));
        Assert.Equal("CONTEXTO",wrong.GetProperty("stage").GetString());
        var cancelledStart=await Post("IniciarEnvio",("fileName",filename+"-cancel.txt"),("totalBytes","1"),("contentType","text/plain"));
        var cancelledId=cancelledStart.GetProperty("uploadId").GetGuid();
        Assert.True((await Post("CancelarEnvio",("uploadId",cancelledId.ToString()))).GetProperty("cancelled").GetBoolean());
        var cancelledPart=await Part(0,new byte[1],cancelledId);
        Assert.False(cancelledPart.TryGetProperty("received",out _));
        Assert.False((await Post("ConcluirEnvio",("uploadId",cancelledId.ToString()))).GetProperty("stored").GetBoolean());
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root.Parent is not null && !File.Exists(Path.Combine(root.FullName,"InovaGed.sln")))root=root.Parent;
        var evidence=Path.Combine(root.FullName,"artifacts/sector-validation/upload-gate.json");
        Directory.CreateDirectory(Path.GetDirectoryName(evidence)!);
        await File.WriteAllTextAsync(evidence,JsonSerializer.Serialize(new { status="passed",interruption=true,malformedPartRejected=true,partReplay=true,contextBound=true,canonicalReceiptCommitRecovered=true,laterVersionNotSubstituted=true,previewHandoffRepairedOnce=true,storedBeforeLinkFailure=true,concurrentCompletion=true,sameFileHash=true,cancelledUploadRejected=true,clinicalData="synthetic" },new JsonSerializerOptions { WriteIndented=true }));
        await Sql($"update ged.sector_document_link set reg_status='I' where tenant_id='{tenant}' and version_id='{version:D}';");

        async Task<JsonElement> Status()=> (await (await user.APIRequest.GetAsync(app+"/DemaisSetores/SituacaoEnvio?uploadId="+upload)).JsonAsync())!.Value;
        Task<JsonElement> Complete()=>Post("ConcluirEnvio",("uploadId",upload.ToString()),("confirmAnotherOccurrence","true"));
        async Task<JsonElement> Part(int index,byte[] bytes,Guid? target=null)
        {
            var form=user.APIRequest.CreateFormData(); form.Set("__RequestVerificationToken",token);
            form.Set("uploadId",(target??upload).ToString());form.Set("chunkIndex",index);
            form.Set("parte",new FilePayload { Name=filename,MimeType="text/plain",Buffer=bytes });
            var response=await user.APIRequest.PostAsync(app+"/DemaisSetores/ParteEnvio",new() { Multipart=form });
            Assert.Equal(200,response.Status);
            return (await response.JsonAsync())!.Value;
        }
        async Task<JsonElement> Post(string action,params (string Key,string Value)[] values)
        {
            var response=await Request(action,values); Assert.Equal(200,response.Status);
            return (await response.JsonAsync())!.Value;
        }
        Task<IAPIResponse> Request(string action,params (string Key,string Value)[] values)
        {
            var form=user.APIRequest.CreateFormData();form.Set("__RequestVerificationToken",token);
            form.Set("patientId",patient);form.Set("encounterId",encounter);form.Set("catalogCode","DS23");
            foreach(var value in values)form.Set(value.Key,value.Value);
            return user.APIRequest.PostAsync(app+"/DemaisSetores/"+action,new() { Form=form });
        }
    }

    private static async Task<string> Sql(string sql)
    {
        var start=new System.Diagnostics.ProcessStartInfo("docker") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
        foreach(var arg in new[] { "exec","inovaged-sector-validation","psql","-X","-q","-A","-t","-v","ON_ERROR_STOP=1","-U","postgres","-d","sector_clean","-c",sql })start.ArgumentList.Add(arg);
        using var process=System.Diagnostics.Process.Start(start)!;
        var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();var result=await output;await errors;
        Assert.Equal(0,process.ExitCode);return result;
    }
}
