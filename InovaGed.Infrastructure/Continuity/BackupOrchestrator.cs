using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Continuity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace InovaGed.Infrastructure.Continuity;

public sealed class BackupOrchestrator(IDbConnectionFactory db, IConfiguration configuration, IPostgresBackupProvider backupProvider, IBackupIntegrityService integrity, IOptions<BackupOptions> options) : IBackupOrchestrator
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(15);
    public async Task<OperationJobDto> EnqueueBackupAsync(Guid? tenantId,Guid? policyId,string requestedBy,string correlationId,CancellationToken ct){var id=Guid.NewGuid();await using var c=await db.OpenAsync(ct);await c.ExecuteAsync(new CommandDefinition("insert into ged.backup_job(id,tenant_id,policy_id,job_type,status,requested_by,correlation_id,next_attempt_at_utc,attempts,max_attempts) values(@id,@tenantId,@policyId,'BACKUP',@status,@requestedBy,@correlationId,now(),0,3)",new{id,tenantId,policyId,status=BackupJobStatuses.Pending,requestedBy,correlationId},cancellationToken:ct));await AddJobEventAsync(c,id,null,BackupJobStatuses.Pending,requestedBy,"Backup solicitado",0,correlationId,ct);return new(id,tenantId,"BACKUP",BackupJobStatuses.Pending,0,"Aguardando worker",DateTime.UtcNow,null,correlationId);}

    public async Task<int> ProcessDueJobsAsync(string workerId,CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        var job=await ClaimAsync(workerId,ct); if(job is null)return 0;
        Guid? backupSetId=null; string? setDir=null;
        try
        {
            ct.ThrowIfCancellationRequested();
            backupSetId=Guid.NewGuid(); setDir=BackupPathSecurity.CreateSetDirectory(options.Value.RootPath,job.TenantId,backupSetId.Value);
            await BeginBackupAsync(job,backupSetId.Value,setDir,workerId,ct);
            if(!await ExtendLeaseAsync(job.Id,workerId,ct))throw new InvalidOperationException("Lease do job perdido antes do pg_dump.");
            var dumpPartial=Path.Combine(setDir,"database.dump.partial");
            var connectionString=configuration.GetConnectionString("DefaultConnection")??throw new InvalidOperationException("ConnectionStrings:DefaultConnection não configurada.");
            var started=DateTime.UtcNow; var result=await backupProvider.DumpAsync(connectionString,dumpPartial,ct);
            if(!result.Success)throw new InvalidOperationException("pg_dump/pg_restore falhou: "+Sanitize(result.SanitizedError));
            var dump=Path.Combine(setDir,"database.dump"); ValidateArtifact(dumpPartial,result.Sha256); File.Move(dumpPartial,dump,true);
            if(!await ExtendLeaseAsync(job.Id,workerId,ct))throw new InvalidOperationException("Lease do job perdido após pg_dump.");
            var manifestPartial=Path.Combine(setDir,"manifest.json.partial");
            var artifact=new BackupManifestArtifact("database.dump",result.Sha256,new FileInfo(dump).Length);
            var manifest=new BackupManifest("1.0",backupSetId.Value,job.TenantId,started,DateTime.UtcNow,"InovaGED","ged","unknown",result.PgDumpVersion,"POSTGRESQL","pg_dump-custom",BackupJobStatuses.Completed,job.CorrelationId??string.Empty,[artifact]);
            await File.WriteAllTextAsync(manifestPartial,JsonSerializer.Serialize(manifest,new JsonSerializerOptions{WriteIndented=true}),ct);
            var manifestHash=await Sha256Async(manifestPartial,ct); var manifestPath=Path.Combine(setDir,"manifest.json"); File.Move(manifestPartial,manifestPath,true);
            var checksumPartial=Path.Combine(setDir,"checksums.sha256.partial");await File.WriteAllTextAsync(checksumPartial,$"{result.Sha256}  database.dump\n{manifestHash}  manifest.json\n",ct);File.Move(checksumPartial,Path.Combine(setDir,"checksums.sha256"),true);
            await CompleteArtifactsAsync(job.Id,backupSetId.Value,setDir,manifestHash,workerId,ct);
            if(options.Value.VerificationEnabled){await UpdateJobAsync(job.Id,BackupJobStatuses.Verifying,workerId,"Verificando checksums",90,ct);var verification=await integrity.VerifyAsync(backupSetId.Value,workerId,ct);if(verification.Status!="VALID")throw new InvalidOperationException("Verificação de integridade rejeitou o backup.");}
            await CompleteJobAsync(job.Id,backupSetId.Value,workerId,ct); return 1;
        }
        catch(OperationCanceledException)
        {
            CleanupPartials(setDir); using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10)); await CancelAsync(job.Id,backupSetId,workerId,timeout.Token); throw;
        }
        catch(Exception ex){CleanupPartials(setDir);await FailOrRetryAsync(job.Id,backupSetId,workerId,ex.Message,CancellationToken.None);return 1;}
    }

    private async Task<OperationJobDto?> ClaimAsync(string workerId,CancellationToken ct){await using var c=await db.OpenAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);const string sql="""with claimed as (select id from ged.backup_job where status in ('PENDING','RETRY') and coalesce(next_attempt_at_utc,now())<=now() and (locked_until_utc is null or locked_until_utc<now()) order by created_at_utc for update skip locked limit 1) update ged.backup_job j set status='CLAIMED',worker_id=@workerId,locked_until_utc=now()+interval '15 minutes',current_step='CLAIMED' from claimed where j.id=claimed.id returning j.id,j.tenant_id TenantId,j.job_type JobType,j.status,coalesce(j.progress_percent,0) ProgressPercent,j.current_step CurrentStep,j.created_at_utc CreatedAtUtc,j.locked_until_utc LockedUntilUtc,j.correlation_id CorrelationId""";var job=await c.QuerySingleOrDefaultAsync<OperationJobDto>(new CommandDefinition(sql,new{workerId},tx,cancellationToken:ct));if(job is not null)await AddJobEventAsync(c,job.Id,null,BackupJobStatuses.Claimed,workerId,"Job reivindicado com FOR UPDATE SKIP LOCKED",5,job.CorrelationId,ct,tx);await tx.CommitAsync(ct);return job;}
    internal async Task<bool> ExtendLeaseAsync(Guid jobId,string workerId,CancellationToken ct){await using var c=await db.OpenAsync(ct);var changed=await c.ExecuteAsync(new CommandDefinition("update ged.backup_job set locked_until_utc=now()+@lease where id=@jobId and worker_id=@workerId and status in ('CLAIMED','RUNNING','VERIFYING') and locked_until_utc>=now()",new{jobId,workerId,lease=LeaseDuration},cancellationToken:ct));if(changed==0)await AddJobEventAsync(c,jobId,null,"LEASE_LOST",workerId,"Lease perdido; processamento interrompido",0,null,ct);return changed==1;}
    private async Task BeginBackupAsync(OperationJobDto job,Guid setId,string setDir,string worker,CancellationToken ct){await using var c=await db.OpenAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);await c.ExecuteAsync(new CommandDefinition("insert into ged.backup_set(id,tenant_id,backup_type,started_at_utc,status,integrity_status,location_masked,location_internal,correlation_id) values(@setId,@tenantId,'POSTGRESQL',now(),'RUNNING','PENDING',@masked,@setDir,@correlationId)",new{setId,tenantId=job.TenantId,masked=$"backup://{setId:N}",setDir,correlationId=job.CorrelationId},tx,cancellationToken:ct));await UpdateJobAsync(c,tx,job.Id,BackupJobStatuses.Running,worker,"pg_dump em execução",15,ct);await tx.CommitAsync(ct);}
    private async Task CompleteArtifactsAsync(Guid jobId,Guid setId,string dir,string hash,string worker,CancellationToken ct){var total=Directory.EnumerateFiles(dir).Sum(x=>new FileInfo(x).Length);await using var c=await db.OpenAsync(ct);await c.ExecuteAsync(new CommandDefinition("update ged.backup_set set size_bytes=@total,file_count=3,manifest_checksum_sha256=@hash where id=@setId",new{setId,total,hash},cancellationToken:ct));}
    private async Task CompleteJobAsync(Guid jobId,Guid setId,string worker,CancellationToken ct){await using var c=await db.OpenAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);await c.ExecuteAsync(new CommandDefinition("update ged.backup_set set status='COMPLETED',finished_at_utc=now() where id=@setId",new{setId},tx,cancellationToken:ct));await UpdateJobAsync(c,tx,jobId,BackupJobStatuses.Completed,worker,"Backup verificado e concluído",100,ct);await tx.CommitAsync(ct);}
    private async Task UpdateJobAsync(Guid id,string status,string worker,string step,int progress,CancellationToken ct){await using var c=await db.OpenAsync(ct);await UpdateJobAsync(c,null,id,status,worker,step,progress,ct);}
    private static async Task UpdateJobAsync(DbConnection c,DbTransaction? tx,Guid id,string status,string worker,string step,int progress,CancellationToken ct){var previous=await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition("select status from ged.backup_job where id=@id",new{id},tx,cancellationToken:ct));if(previous is null||!BackupJobStatuses.CanTransition(previous,status))throw new InvalidOperationException($"Transição de backup inválida: {previous ?? "NOT_FOUND"} -> {status}");await c.ExecuteAsync(new CommandDefinition("update ged.backup_job set status=@status,worker_id=@worker,current_step=@step,progress_percent=@progress,locked_until_utc=case when @terminal then null else now()+interval '15 minutes' end,finished_at_utc=case when @terminal then now() else finished_at_utc end where id=@id and worker_id=@worker",new{id,status,worker,step,progress,terminal=BackupJobStatuses.Terminal.Contains(status)},tx,cancellationToken:ct));await AddJobEventAsync(c,id,previous,status,worker,step,progress,null,ct,tx);}
    private async Task FailOrRetryAsync(Guid id,Guid? setId,string worker,string reason,CancellationToken ct){await using var c=await db.OpenAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);var outcome=await c.QuerySingleAsync<(string Status,int Attempts,int MaxAttempts)>(new CommandDefinition("update ged.backup_job set attempts=attempts+1,status=case when attempts+1>=max_attempts then 'DEAD_LETTER' else 'RETRY' end,current_step=@reason,locked_until_utc=null,next_attempt_at_utc=now()+interval '5 minutes' where id=@id and worker_id=@worker returning status Status,attempts Attempts,max_attempts MaxAttempts",new{id,worker,reason=Sanitize(reason)},tx,cancellationToken:ct));if(setId.HasValue)await c.ExecuteAsync(new CommandDefinition("update ged.backup_set set status='FAILED',finished_at_utc=now() where id=@setId",new{setId},tx,cancellationToken:ct));await AddJobEventAsync(c,id,null,outcome.Status,worker,$"Falha sanitizada; tentativa {outcome.Attempts}/{outcome.MaxAttempts}",0,null,ct,tx);await tx.CommitAsync(ct);}
    private async Task CancelAsync(Guid id,Guid? setId,string worker,CancellationToken ct){await using var c=await db.OpenAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);if(setId.HasValue)await c.ExecuteAsync(new CommandDefinition("update ged.backup_set set status='FAILED',finished_at_utc=now() where id=@setId",new{setId},tx,cancellationToken:ct));await UpdateJobAsync(c,tx,id,BackupJobStatuses.Cancelled,worker,"Cancelamento confirmado",0,ct);await tx.CommitAsync(ct);}
    private static Task AddJobEventAsync(DbConnection c,Guid id,string? oldStatus,string newStatus,string worker,string reason,int progress,string? correlation,CancellationToken ct,DbTransaction? tx=null)=>c.ExecuteAsync(new CommandDefinition("insert into ged.operation_job_event(job_id,old_status,new_status,worker_id,reason,progress_percent,correlation_id) values(@id,@oldStatus,@newStatus,@worker,@reason,@progress,@correlation)",new{id,oldStatus,newStatus,worker,reason,progress,correlation},tx,cancellationToken:ct));
    private static async Task<string> Sha256Async(string path,CancellationToken ct){await using var fs=File.OpenRead(path);return Convert.ToHexString(await SHA256.HashDataAsync(fs,ct)).ToLowerInvariant();}
    private static void ValidateArtifact(string path,string expected){if(!File.Exists(path)||new FileInfo(path).Length<=0)throw new InvalidDataException("Artefato de backup vazio ou ausente.");var actual=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();if(!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual),Convert.FromHexString(expected)))throw new InvalidDataException("Checksum produzido pelo provider não confere.");}
    private static void CleanupPartials(string? dir){if(string.IsNullOrWhiteSpace(dir)||!Directory.Exists(dir))return;foreach(var file in Directory.EnumerateFiles(dir,"*.partial")){try{File.Delete(file);}catch(IOException){}}}
    private static string Sanitize(string value){if(string.IsNullOrWhiteSpace(value))return "Falha sem detalhes seguros.";var clean=value.Split('\n','\r')[0].Replace("password","p******d",StringComparison.OrdinalIgnoreCase).Replace("token","t***n",StringComparison.OrdinalIgnoreCase);return clean[..Math.Min(clean.Length,500)];}
}
