using System.Net;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Labels.Printing;

namespace InovaGed.Infrastructure.PhysicalArchive;

public sealed class LabelPrintJobService(IDbConnectionFactory dbFactory) : ILabelPrintJobService
{
    public async Task<Guid> CreateJobAsync(LabelPrintJobCreateCommand x, CancellationToken ct)
    {
        Validate(x.TenantId, x.RequestedBy, x.Copies, x.PayloadJson);
        await using var db = await dbFactory.OpenAsync(ct);
        if (x.SubjectId.HasValue) await RequireReprintReason(db, x.TenantId, x.SubjectType, x.SubjectId.Value, x.TemplateCode, x.ReprintReason, ct);
        var id=Guid.NewGuid();
        await db.ExecuteAsync(new CommandDefinition("""
insert into ged.label_print_job(id,tenant_id,job_number,print_mode,template_code,template_name,subject_type,subject_id,control_number,location,copies,status,payload_json,reprint_reason,requested_by,requested_ip,requested_user_agent)
values(@id,@TenantId,@number,@PrintMode,@TemplateCode,@TemplateName,@SubjectType,@SubjectId,@ControlNumber,@Location,@Copies,'PENDING',cast(@PayloadJson as jsonb),nullif(@ReprintReason,''),@RequestedBy,cast(@IpAddress as inet),@UserAgent)
""",new{id,x.TenantId,number=JobNumber(),x.PrintMode,x.TemplateCode,x.TemplateName,x.SubjectType,x.SubjectId,x.ControlNumber,x.Location,x.Copies,x.PayloadJson,x.ReprintReason,x.RequestedBy,x.IpAddress,x.UserAgent},cancellationToken:ct));
        return id;
    }

    public async Task<Guid> CreateBatchJobAsync(LabelPrintBatchJobCreateCommand x, CancellationToken ct)
    {
        Validate(x.TenantId,x.RequestedBy,x.Copies,"{}");
        if(x.Items.Count==0) throw new InvalidOperationException("Selecione ao menos um item para impressão.");
        await using var db=await dbFactory.OpenAsync(ct); await using var tx=await db.BeginTransactionAsync(ct);
        foreach(var item in x.Items.Where(i=>i.SubjectId.HasValue)) await RequireReprintReason(db,x.TenantId,item.SubjectType,item.SubjectId!.Value,x.TemplateCode,x.ReprintReason,ct,tx);
        var id=Guid.NewGuid(); var payload=$"{{\"itemCount\":{x.Items.Count}}}";
        await db.ExecuteAsync(new CommandDefinition("""
insert into ged.label_print_job(id,tenant_id,job_number,print_mode,template_code,template_name,subject_type,batch_id,copies,status,payload_json,reprint_reason,requested_by,requested_ip,requested_user_agent)
values(@id,@TenantId,@number,@PrintMode,@TemplateCode,@TemplateName,@SubjectType,@id,@Copies,'PENDING',cast(@payload as jsonb),nullif(@ReprintReason,''),@RequestedBy,cast(@IpAddress as inet),@UserAgent)
""",new{id,x.TenantId,number=JobNumber(),x.PrintMode,x.TemplateCode,x.TemplateName,x.SubjectType,x.Copies,payload,x.ReprintReason,x.RequestedBy,x.IpAddress,x.UserAgent},tx,cancellationToken:ct));
        foreach(var item in x.Items) await db.ExecuteAsync(new CommandDefinition("""
insert into ged.label_print_job_item(tenant_id,job_id,subject_type,subject_id,control_number,location,payload_json,display_order)
values(@TenantId,@id,@SubjectType,@SubjectId,@ControlNumber,@Location,cast(@PayloadJson as jsonb),@DisplayOrder)
""",new{x.TenantId,id,item.SubjectType,item.SubjectId,item.ControlNumber,item.Location,item.PayloadJson,item.DisplayOrder},tx,cancellationToken:ct));
        await tx.CommitAsync(ct); return id;
    }

    public async Task<LabelPrintJobDetails?> GetAsync(Guid tenantId,Guid jobId,CancellationToken ct)
    {
        await using var db=await dbFactory.OpenAsync(ct);
        var row=await db.QuerySingleOrDefaultAsync<JobRow>(new CommandDefinition("""
select j.id,j.tenant_id TenantId,j.job_number JobNumber,j.print_mode PrintMode,j.template_code TemplateCode,j.template_name TemplateName,
j.subject_type SubjectType,j.subject_id SubjectId,j.control_number ControlNumber,j.location,j.copies,j.status,j.payload_json::text PayloadJson,
j.pdf_path PdfPath,j.error_message ErrorMessage,j.requested_by RequestedBy,j.requested_at RequestedAt,j.printed_by PrintedBy,
j.printed_at PrintedAt,j.cancel_reason CancelReason,j.reprint_reason ReprintReason,u.name RequestedByName
from ged.label_print_job j left join ged.app_user u on u.tenant_id=j.tenant_id and u.id=j.requested_by
where j.tenant_id=@tenantId and j.id=@jobId and j.reg_status='A'
""",new{tenantId,jobId},cancellationToken:ct));
        if(row is null)return null;
        var items=(await db.QueryAsync<LabelPrintJobItemDetails>(new CommandDefinition("""
select id,subject_id SubjectId,subject_type SubjectType,control_number ControlNumber,location,payload_json::text PayloadJson,status,display_order DisplayOrder,printed_at PrintedAt,error_message ErrorMessage
from ged.label_print_job_item where tenant_id=@tenantId and job_id=@jobId and reg_status='A' order by display_order
""",new{tenantId,jobId},cancellationToken:ct))).AsList();
        return new(row.Id,row.TenantId,row.JobNumber,row.PrintMode,row.TemplateCode,row.TemplateName,row.SubjectType,row.SubjectId,row.ControlNumber,row.Location,row.Copies,row.Status,row.PayloadJson,row.PdfPath,row.ErrorMessage,row.RequestedBy,row.RequestedAt,row.PrintedBy,row.PrintedAt,row.CancelReason,row.ReprintReason,row.RequestedByName,items);
    }

    public async Task<IReadOnlyList<LabelPrintJobListItem>> ListAsync(Guid tenantId,LabelPrintJobFilter f,CancellationToken ct)
    {
        await using var db=await dbFactory.OpenAsync(ct);
        return (await db.QueryAsync<LabelPrintJobListItem>(new CommandDefinition("""
select j.id,j.job_number JobNumber,j.print_mode PrintMode,j.template_code TemplateCode,j.template_name TemplateName,j.subject_type SubjectType,
j.control_number ControlNumber,j.copies,j.status,j.requested_at RequestedAt,j.printed_at PrintedAt,u.name RequestedByName,j.reprint_reason ReprintReason,
case when j.batch_id is null then 1 else (select count(*) from ged.label_print_job_item i where i.tenant_id=j.tenant_id and i.job_id=j.id and i.reg_status='A') end::int ItemCount
from ged.label_print_job j left join ged.app_user u on u.tenant_id=j.tenant_id and u.id=j.requested_by
where j.tenant_id=@tenantId and j.reg_status='A' and (@From is null or j.requested_at>=@From) and (@To is null or j.requested_at<@To + interval '1 day')
and (@UserId is null or j.requested_by=@UserId) and (coalesce(@TemplateCode,'')='' or j.template_code=@TemplateCode)
and (coalesce(@SubjectType,'')='' or j.subject_type=@SubjectType) and (coalesce(@ControlNumber,'')='' or j.control_number ilike '%'||@ControlNumber||'%')
and (coalesce(@Status,'')='' or j.status=@Status) order by j.requested_at desc limit 500
""",new{tenantId,f.From,f.To,f.UserId,f.TemplateCode,f.SubjectType,f.ControlNumber,f.Status},cancellationToken:ct))).AsList();
    }

    public Task MarkPreviewedAsync(Guid tenantId,Guid jobId,Guid userId,CancellationToken ct)=>SetStatus(tenantId,jobId,"status='PREVIEWED'",new[]{LabelPrintJobStatus.Pending,LabelPrintJobStatus.Previewed},ct);
    public async Task MarkPrintedAsync(Guid tenantId,Guid jobId,Guid userId,CancellationToken ct)
    {
        if(userId==Guid.Empty)throw new InvalidOperationException("Usuário autenticado obrigatório.");
        await using var db=await dbFactory.OpenAsync(ct); await using var tx=await db.BeginTransactionAsync(ct);
        var job=await db.QuerySingleOrDefaultAsync<PrintRow>(new CommandDefinition("select * from ged.label_print_job where tenant_id=@tenantId and id=@jobId and reg_status='A' for update",new{tenantId,jobId},tx,cancellationToken:ct))??throw new KeyNotFoundException("Job não encontrado.");
        if(job.status is LabelPrintJobStatus.Cancelled or LabelPrintJobStatus.Printed) throw new InvalidOperationException("Este job não pode mais ser impresso.");
        var items=(await db.QueryAsync<ItemRow>(new CommandDefinition("select * from ged.label_print_job_item where tenant_id=@tenantId and job_id=@jobId and reg_status='A' order by display_order",new{tenantId,jobId},tx,cancellationToken:ct))).AsList();
        if(items.Count==0) items.Add(new ItemRow { id=Guid.NewGuid(),subject_id=job.subject_id,subject_type=job.subject_type,control_number=job.control_number,location=job.location,payload_json=job.payload_json });
        foreach(var item in items)
        {
            var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.payload_json))).ToLowerInvariant();
            await db.ExecuteAsync(new CommandDefinition("""
insert into ged.label_print_history(id,tenant_id,label_subject_type,label_subject_id,template_code,snapshot_json,snapshot_sha256,printed_by,ip_address,user_agent,reprint_reason,print_job_id,print_job_item_id,print_mode,generated_path)
values(gen_random_uuid(),@tenantId,@type,@subjectId,@template,cast(@json as jsonb),@hash,@userId,cast(@ip as inet),@agent,nullif(@reason,''),@jobId,@itemId,@mode,@path)
""",new{tenantId,type=item.subject_type,subjectId=item.subject_id,template=job.template_code,json=item.payload_json,hash,userId,ip=job.requested_ip,agent=job.requested_user_agent,reason=job.reprint_reason,jobId,itemId=items.Count==1&&job.batch_id is null?(Guid?)null:item.id,mode=job.print_mode,path=job.pdf_path},tx,cancellationToken:ct));
        }
        await db.ExecuteAsync(new CommandDefinition("update ged.label_print_job_item set status='PRINTED',printed_at=now() where tenant_id=@tenantId and job_id=@jobId and reg_status='A'; update ged.label_print_job set status='PRINTED',printed_by=@userId,printed_at=now() where tenant_id=@tenantId and id=@jobId",new{tenantId,jobId,userId},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);
    }
    public async Task CancelAsync(Guid tenantId,Guid jobId,Guid userId,string reason,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(reason))throw new InvalidOperationException("Informe o motivo do cancelamento.");
        await using var db=await dbFactory.OpenAsync(ct); var count=await db.ExecuteAsync(new CommandDefinition("update ged.label_print_job set status='CANCELLED',cancelled_by=@userId,cancelled_at=now(),cancel_reason=@reason where tenant_id=@tenantId and id=@jobId and status not in ('PRINTED','CANCELLED')",new{tenantId,jobId,userId,reason},cancellationToken:ct));
        if(count==0)throw new InvalidOperationException("Job não encontrado ou já finalizado.");
    }
    private async Task SetStatus(Guid tenantId,Guid jobId,string set,IReadOnlyList<string> allowed,CancellationToken ct){await using var db=await dbFactory.OpenAsync(ct);var n=await db.ExecuteAsync(new CommandDefinition($"update ged.label_print_job set {set},error_message=null where tenant_id=@tenantId and id=@jobId and status=any(@allowed)",new{tenantId,jobId,allowed},cancellationToken:ct));if(n==0&&await GetAsync(tenantId,jobId,ct) is null)throw new KeyNotFoundException("Job não encontrado.");}
    private static async Task RequireReprintReason(System.Data.IDbConnection db,Guid tenantId,string type,Guid id,string template,string? reason,CancellationToken ct,System.Data.IDbTransaction? tx=null){var n=await db.ExecuteScalarAsync<int>(new CommandDefinition("select count(*) from ged.label_print_history where tenant_id=@tenantId and label_subject_type=@type and label_subject_id=@id and template_code=@template",new{tenantId,type,id,template},tx,cancellationToken:ct));if(n>0&&string.IsNullOrWhiteSpace(reason))throw new InvalidOperationException("Esta etiqueta já foi impressa anteriormente. Para reimprimir, informe o motivo.");}
    private static void Validate(Guid tenant,Guid user,int copies,string json){if(tenant==Guid.Empty||user==Guid.Empty)throw new InvalidOperationException("Tenant e usuário são obrigatórios.");if(copies is <1 or >500)throw new InvalidOperationException("Quantidade de cópias inválida.");ArgumentException.ThrowIfNullOrWhiteSpace(json);}
    private static string JobNumber()=>$"LBL-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000,9999)}";
    private sealed class JobRow { public Guid Id{get;set;} public Guid TenantId{get;set;} public string JobNumber{get;set;}="";public string PrintMode{get;set;}="";public string TemplateCode{get;set;}="";public string? TemplateName{get;set;}public string SubjectType{get;set;}="";public Guid? SubjectId{get;set;}public string? ControlNumber{get;set;}public string? Location{get;set;}public int Copies{get;set;}public string Status{get;set;}="";public string PayloadJson{get;set;}="";public string? PdfPath{get;set;}public string? ErrorMessage{get;set;}public Guid? RequestedBy{get;set;}public DateTime RequestedAt{get;set;}public Guid? PrintedBy{get;set;}public DateTime? PrintedAt{get;set;}public string? CancelReason{get;set;}public string? ReprintReason{get;set;}public string? RequestedByName{get;set;} }
    private sealed class PrintRow {public Guid? subject_id{get;set;}public Guid? batch_id{get;set;}public string subject_type{get;set;}="";public string template_code{get;set;}="";public string print_mode{get;set;}="";public string payload_json{get;set;}="";public string? control_number{get;set;}public string? location{get;set;}public string? requested_ip{get;set;}public string? requested_user_agent{get;set;}public string? reprint_reason{get;set;}public string? pdf_path{get;set;}public string status{get;set;}="";}
    private sealed class ItemRow {public Guid id{get;set;}public Guid? subject_id{get;set;}public string subject_type{get;set;}="";public string? control_number{get;set;}public string? location{get;set;}public string payload_json{get;set;}="";}
}

public sealed class LabelHtmlPdfRenderService(IDbConnectionFactory dbFactory,ILabelPrintJobService jobs) : ILabelPdfRenderService
{
    public async Task<LabelPdfResult> GeneratePdfAsync(Guid tenantId,Guid jobId,CancellationToken ct)
    {
        var job=await jobs.GetAsync(tenantId,jobId,ct)??throw new KeyNotFoundException("Job não encontrado.");
        var labels=job.Items.Count==0?new[]{(job.ControlNumber,job.Location,job.PayloadJson)}:job.Items.Select(x=>(x.ControlNumber,x.Location,x.PayloadJson));
        var body=string.Join("",labels.SelectMany(x=>Enumerable.Range(0,job.Copies).Select(_=>$"<article class=\"label\"><strong>{WebUtility.HtmlEncode(x.ControlNumber??job.TemplateName)}</strong><span>{WebUtility.HtmlEncode(x.Location)}</span><small>{WebUtility.HtmlEncode(x.PayloadJson)}</small></article>")));
        var encodedTitle = WebUtility.HtmlEncode(job.JobNumber);
        var html = $$"""
<!doctype html>
<html lang="pt-BR">
<head>
    <meta charset="utf-8">
    <title>{{encodedTitle}}</title>
    <style>
        @page {
            size: A4;
            margin: 0;
        }

        html,
        body {
            margin: 0;
            padding: 0;
            background: #fff;
            font-family: Arial, Helvetica, sans-serif;
            -webkit-print-color-adjust: exact;
            print-color-adjust: exact;
        }

        .page {
            box-sizing: border-box;
            width: 210mm;
            min-height: 297mm;
            padding: 10mm;
            display: grid;
            grid-template-columns: repeat(2, 1fr);
            gap: 4mm;
        }

        .label {
            border: 1px solid #111;
            padding: 6mm;
            min-height: 55mm;
            display: flex;
            flex-direction: column;
            gap: 3mm;
            overflow: hidden;
            page-break-inside: avoid;
            break-inside: avoid;
        }

        .label strong {
            font-size: 14pt;
        }

        .label span {
            font-size: 10pt;
        }

        .label small {
            word-break: break-word;
            font-size: 8pt;
        }

        @media print {
            .no-print {
                display: none !important;
            }
        }
    </style>
</head>
<body>
    <main class="page">
        {{body}}
    </main>
</body>
</html>
""";
        await using var db=await dbFactory.OpenAsync(ct);await db.ExecuteAsync(new CommandDefinition("update ged.label_print_job set status='PDF_GENERATED',pdf_path=@path,error_message=null where tenant_id=@tenantId and id=@jobId and status not in ('PRINTED','CANCELLED')",new{tenantId,jobId,path=$"label-jobs/{job.JobNumber}.html"},cancellationToken:ct));
        return new(Encoding.UTF8.GetBytes(html),"text/html; charset=utf-8",$"{job.JobNumber}.html",false);
    }
}
