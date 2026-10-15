using System.Text.Json;
using Dapper;
using InovaGed.Application.ArtificialIntelligence;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Sectors;
using InovaGed.Infrastructure.ArtificialIntelligence;
using InovaGed.Infrastructure.Ged.Documents;
using InovaGed.Infrastructure.Security;
using InovaGed.Infrastructure.Sectors;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace InovaGed.Application.Tests.Sectors;

public sealed partial class SectorEntryAndOpeningGateTests
{
    private static async Task VerifyClinicalExtractionAsync(NpgsqlConnection conn,Factory factory,Guid tenant,Guid user,Guid patient,Guid encounter)
    {
        var doc=Guid.NewGuid();var version=Guid.NewGuid();var link=Guid.NewGuid();var role=Guid.NewGuid();
        var args=new{tenant,user,patient,encounter,doc,version,link,role,text="--- página 2 ---\n"+SectorClinicalEvidenceTests.Evidence+"\nOutro medicamento sintético 20 mg via oral."};
        await conn.ExecuteAsync("""
insert into ged.document(id,tenant_id,code,title,is_confidential,created_by) values(@doc,@tenant,'CLINICAL-FIXTURE','Documento sintético de teste',false,@user);
insert into ged.document_version(id,tenant_id,document_id,version_number,file_name,file_extension,file_size_bytes,storage_path,checksum_sha256,content_type,content_text,created_by)
values(@version,@tenant,@doc,1,'synthetic.pdf','.pdf',100,'synthetic.pdf',repeat('b',64),'application/pdf',@text,@user);
insert into ged.sector_document_link(id,tenant_id,patient_id,encounter_id,document_id,version_id,catalog_code,origin,original_file_name,content_sha256,uploaded_by,transfer_status,preview_status,ocr_status,extraction_status)
values(@link,@tenant,@patient,@encounter,@doc,@version,'DS23','GED_UPLOAD','synthetic.pdf',repeat('b',64),@user,'GRAVADO','AGUARDANDO','NAO_APLICAVEL','AGUARDANDO');
""",args);
        var auth=new AbacAuthorizationService(factory);
        var sectors=new DemaisSetoresService(factory,null!,null!,null!,null!,null!,auth,new Audit(),Options.Create(new DocumentUploadOptions()),new UnconfiguredClinicalSource(),NullLogger<DemaisSetoresService>.Instance);
        var fixture=new ClinicalFixtureGateway();
        var service=new SectorClinicalExtractionService(factory,sectors,auth,fixture,fixture,Options.Create(new DocumentAiOptions()));
        Assert.Equal(403,(await service.ExtractAsync(tenant,user,link,"denied",CancellationToken.None)).Status);
        Assert.Equal(0,fixture.Sends);
        await conn.ExecuteAsync("""
insert into ged.role(id,tenant_id,code,name) values(@role,@tenant,'HOSPITAL','Hospital');
insert into ged.app_role(id,tenant_id,name,normalized_name) values(@role,@tenant,'Hospital','HOSPITAL');
insert into ged.user_role(user_id,role_id) values(@user,@role);
insert into ged.permission(code,name) values('Documents.View','View') on conflict(code) do nothing;
insert into ged.role_permission(tenant_id,role_id,permission_code) values(@tenant,@role,'Documents.View');
insert into ged.sector_access(tenant_id,user_id,sector_code,permission_code) values(@tenant,@user,'FATURAMENTO','ADMINISTRAR');
""",args);
        Assert.Equal(403,(await service.ExtractAsync(Guid.NewGuid(),user,link,"wrong-tenant",CancellationToken.None)).Status);
        await conn.ExecuteAsync("update ged.app_user set is_locked=true where id=@user",args);
        Assert.Equal(403,(await service.ExtractAsync(tenant,user,link,"locked",CancellationToken.None)).Status);
        await conn.ExecuteAsync("update ged.app_user set is_locked=false where id=@user",args);
        var first=await service.ExtractAsync(tenant,user,link,"first",CancellationToken.None);
        Assert.Equal(200,first.Status);Assert.Equal(2,first.Items);Assert.Equal(1,fixture.Sends);
        Assert.Equal(200,(await service.ExtractAsync(tenant,user,link,"first",CancellationToken.None)).Status);
        Assert.Equal(200,(await service.ExtractAsync(tenant,user,link,"another-key",CancellationToken.None)).Status);
        Assert.Equal(1,fixture.Sends);
        var field=await conn.ExecuteScalarAsync<Guid>("select id from ged.sector_extracted_field where tenant_id=@tenant and link_id=@link and mechanism='IA_CLINICA_V1' and evidence_excerpt=@evidence",new{tenant,link,evidence=SectorClinicalEvidenceTests.Evidence});
        Assert.Equal("TEXTO_NATIVO",await conn.ExecuteScalarAsync<string>("select source_kind from ged.sector_extracted_field where id=@field",new{field}));
        Assert.Equal(2,await conn.ExecuteScalarAsync<int>("select page_number from ged.sector_extracted_field where id=@field",new{field}));
        Assert.Null(await sectors.ReviewFieldAsync(new(tenant,user,false,field,"CORRIGIDO","Valor revisado sintético","", ""),CancellationToken.None));
        var controller=new InovaGed.Web.Controllers.DemaisSetoresController(sectors,null!,null!,Options.Create(new DocumentUploadOptions()),new SessionUser(true,tenant,user),factory,null!,auth);
        controller.ControllerContext=new Microsoft.AspNetCore.Mvc.ControllerContext{HttpContext=new Microsoft.AspNetCore.Http.DefaultHttpContext()};
        var view=Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(await controller.Consultar(patient,encounter,null,null,null,CancellationToken.None));
        var reviewed=Assert.Single(((IReadOnlyList<InovaGed.Web.Controllers.SectorFieldRow>)view.ViewData["Fields"]!).Where(x=>x.FieldId==field));
        Assert.Equal(user,reviewed.ReviewerId);Assert.Equal("Pessoa sintética",reviewed.ReviewerName);Assert.EndsWith("UTC",reviewed.ReviewedAt);

        var queue=new GedProcessingJobRepository(factory,NullLogger<GedProcessingJobRepository>.Instance);
        var processor=new SectorVersionProcessor(factory);
        await DrainLocal(); // Late local processing must not supersede a current AI result.
        await ChangeText("second");
        Assert.Equal(409,(await service.ExtractAsync(tenant,user,link,"first",CancellationToken.None)).Status);
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.BeforeResponse=async _=>{entered.SetResult();await release.Task;};
        var pending=service.ExtractAsync(tenant,user,link,"parallel",CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(202,(await service.ExtractAsync(tenant,user,link,"parallel-other",CancellationToken.None)).Status);
        release.SetResult();Assert.Equal(200,(await pending).Status);fixture.BeforeResponse=null;
        Assert.Equal("Valor revisado sintético",await conn.ExecuteScalarAsync<string>("select confirmed_value from ged.sector_extracted_field where id=@field",new{field}));
        Assert.Equal(1,await conn.ExecuteScalarAsync<int>("select count(*) from ged.sector_field_review where field_id=@field",new{field}));

        await ChangeText("stale");
        fixture.BeforeResponse=async _=>await ChangeText("changed-during-provider");
        Assert.Equal(409,(await service.ExtractAsync(tenant,user,link,"stale",CancellationToken.None)).Status);
        fixture.BeforeResponse=null;
        fixture.WrongSource=true;
        Assert.Equal(422,(await service.ExtractAsync(tenant,user,link,"wrong-source",CancellationToken.None)).Status);
        fixture.WrongSource=false;
        fixture.InvalidEvidence=true;
        Assert.Equal(422,(await service.ExtractAsync(tenant,user,link,"invented",CancellationToken.None)).Status);
        fixture.InvalidEvidence=false;
        fixture.RemoteUnknown=true;
        fixture.BeforeResponse=_=>throw new OperationCanceledException("Synthetic interruption after reservation");
        await Assert.ThrowsAsync<OperationCanceledException>(()=>service.ExtractAsync(tenant,user,link,"unknown",CancellationToken.None));
        fixture.BeforeResponse=null;
        var unknownId=await conn.ExecuteScalarAsync<Guid>("select id from ged.sector_clinical_extraction where tenant_id=@tenant and request_key='unknown'",args);
        var sends=fixture.Sends;
        Assert.Equal(202,(await service.ExtractAsync(tenant,user,link,"unknown-new-key",CancellationToken.None)).Status);
        Assert.Equal(202,(await service.ExtractAsync(tenant,user,link,"resume",CancellationToken.None,unknownId)).Status);
        Assert.Equal(sends,fixture.Sends);
        var execution=await conn.ExecuteScalarAsync<Guid>("select execution_id from ged.sector_clinical_extraction where id=@id",new{id=unknownId});
        fixture.Resolve(execution);
        Assert.Equal(200,(await service.ExtractAsync(tenant,user,link,"resume",CancellationToken.None,unknownId)).Status);
        Assert.Equal(sends,fixture.Sends);
        var currentField=await conn.ExecuteScalarAsync<Guid>("select id from ged.sector_extracted_field where link_id=@link and mechanism='IA_CLINICA_V1' and status='SUGERIDO'",args);
        await DrainLocal();
        Assert.Equal("SUGERIDO",await conn.ExecuteScalarAsync<string>("select status from ged.sector_extracted_field where id=@id",new{id=currentField}));
        await ChangeText("revoked");
        fixture.BeforeResponse=async _=>await conn.ExecuteAsync("update ged.app_user set is_locked=true where id=@user",args);
        Assert.Equal(403,(await service.ExtractAsync(tenant,user,link,"revoked",CancellationToken.None)).Status);
        fixture.BeforeResponse=null;
        await conn.ExecuteAsync("update ged.app_user set is_locked=false where id=@user",args);
        await DrainLocal();
        Assert.Equal("SUPERADO",await conn.ExecuteScalarAsync<string>("select status from ged.sector_extracted_field where id=@id",new{id=currentField}));
        Assert.NotNull(await sectors.ReviewFieldAsync(new(tenant,user,false,currentField,"CONFIRMADO",null,"",""),CancellationToken.None));
        Assert.Equal("CORRIGIDO",await conn.ExecuteScalarAsync<string>("select status from ged.sector_extracted_field where id=@field",new{field}));
        await conn.ExecuteAsync("update ged.sector_document_link set reg_status='I' where id=@link",args);

        Task ChangeText(string marker)=>conn.ExecuteAsync("update ged.document_version set content_text=@text||chr(10)||@marker where id=@version",new{args.text,version,marker});
        async Task DrainLocal()
        {
            foreach(var job in await queue.DequeueAsync("clinical-test",10,CancellationToken.None))await processor.ProcessAsync(job,CancellationToken.None);
        }
    }

    private sealed class ClinicalFixtureGateway : IDocumentAiGateway,IAiGovernanceStore
    {
        private readonly Dictionary<string,Guid> keys=[];
        private readonly Dictionary<Guid,AiExecutionStatus> executions=[];
        public int Sends;public bool WrongSource,InvalidEvidence,RemoteUnknown;
        public Func<AiRequest,Task>? BeforeResponse;
        public IReadOnlyDictionary<string,AiCapabilities> Capabilities=>new Dictionary<string,AiCapabilities>();
        public int MaxOutputTokens=>1000;
        public async Task<AiResult> ExecuteAsync(AiRequest request,CancellationToken ct)
        {
            Assert.Equal(AiTask.ExtractClinicalItems,request.Task);
            Assert.Single(request.SourceDocuments);Assert.Single(request.Context);
            Assert.True(request.Context[0].Text.Contains(SectorClinicalEvidenceTests.Evidence,StringComparison.Ordinal));
            if(keys.TryGetValue(request.IdempotencyKey!,out var previous))
            {
                if(request.OnExecutionReserved is not null)await request.OnExecutionReserved(previous,ct);
                return executions[previous].Result!;
            }
            Sends++;var id=Guid.NewGuid();keys.Add(request.IdempotencyKey!,id);
            var item=SectorClinicalEvidenceTests.Item();if(InvalidEvidence)item["dose"]="999 mg";
            var second=SectorClinicalEvidenceTests.Item("Outro medicamento sintético 20 mg via oral.");
            second["name"]="Outro medicamento sintético";second["dose"]="20 mg";
            foreach(var key in new[]{"frequency","quantity","date"})second[key]="";
            var output=JsonDocument.Parse(new System.Text.Json.Nodes.JsonObject{["items"]=new System.Text.Json.Nodes.JsonArray(item,second)}.ToJsonString());
            var result=new AiResult(!RemoteUnknown,null,output,new AiUsage(10,10,20),"Synthetic","fixture-v1",
                RemoteUnknown?AiFailureKind.Timeout:AiFailureKind.None,ProviderReached:true,ExecutionId:id);
            executions[id]=new(id,request.TenantId,request.UserId,request.Task,RemoteUnknown?AiExecutionState.RemoteOutcomeUnknown:AiExecutionState.Completed,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,result,
                WrongSource?[new(Guid.NewGuid(),Guid.NewGuid())]:request.SourceDocuments,DateTimeOffset.UtcNow.AddHours(1),AiSourceIntegrity.Resolved);
            if(request.OnExecutionReserved is not null)await request.OnExecutionReserved(id,ct);
            if(BeforeResponse is not null)await BeforeResponse(request);
            return result;
        }
        public void Resolve(Guid id) { var old=executions[id];executions[id]=old with {State=AiExecutionState.Completed,Result=old.Result! with{Success=true,Failure=AiFailureKind.None}};RemoteUnknown=false; }
        public Task<AiExecutionStatus?> GetExecutionAsync(Guid tenantId,Guid userId,Guid id,CancellationToken ct)=>Task.FromResult(executions.TryGetValue(id,out var value)&&value.TenantId==tenantId&&value.UserId==userId?(AiExecutionStatus?)value:null);
        public Task<AiEffectivePolicy?> GetEffectivePolicyAsync(Guid t,AiTask task,CancellationToken ct)=>throw new NotSupportedException();
        public Task<AiExecutionLease> ReserveAsync(AiRequest r,string p,string m,long revision,long estimate,CancellationToken ct)=>throw new NotSupportedException();
        public Task<bool> MarkRunningAsync(Guid id,CancellationToken ct)=>throw new NotSupportedException();
        public Task<bool> MarkSentAsync(Guid id,CancellationToken ct)=>throw new NotSupportedException();
        public Task CompleteAsync(Guid id,AiResult r,long tokens,TimeSpan duration,CancellationToken ct)=>throw new NotSupportedException();
        public Task MigrateVerifiedLegacySourcesAsync(Guid t,Guid u,Guid id,IReadOnlyList<AiExecutionSource> s,CancellationToken ct)=>throw new NotSupportedException();
        public Task<int> ExpireReservationsAsync(CancellationToken ct)=>throw new NotSupportedException();
        public Task<AiRecoveryHealth> GetRecoveryHealthAsync(CancellationToken ct)=>throw new NotSupportedException();
    }
}
