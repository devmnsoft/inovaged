using System.Text.Json;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Continuity;
using InovaGed.Application.SystemHealth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InovaGed.Infrastructure.Continuity;

public sealed class ContinuityRepository(IDbConnectionFactory db, IOptions<BackupOptions> backupOptions, IOptions<PortabilityOptions> portabilityOptions) : IBackupPolicyService, IBackupCatalogService, IRecoveryObjectiveService, IPortabilityExportService, IRecoveryPlanService, ITenantOffboardingService, IDataDeletionWorkflowService
{
    public async Task<IReadOnlyList<BackupPolicyDto>> ListPoliciesAsync(Guid? tenantId, CancellationToken ct) { await using var c = await db.OpenAsync(ct); var rows = await c.QueryAsync<BackupPolicyDto>("select id, tenant_id TenantId, name, scope, enabled, backup_type BackupType, frequency, scheduled_at ScheduledAt, timezone TimeZone, retention_days RetentionDays, destination_kind DestinationKind, encryption_enabled EncryptionEnabled, auto_verification_enabled AutoVerificationEnabled, auto_restore_test_allowed AutoRestoreTestAllowed, rpo_minutes RpoMinutes, rto_minutes RtoMinutes, status, created_at_utc CreatedAtUtc, updated_at_utc UpdatedAtUtc from ged.backup_policy where (@tenant is null or tenant_id=@tenant or tenant_id is null) order by created_at_utc desc", new { tenant = tenantId }); return rows.AsList(); }
    public async Task<BackupPolicyDto> SaveAsync(BackupPolicyDto p, string userName, string justification, CancellationToken ct) { await using var c = await db.OpenAsync(ct); var id = p.Id == Guid.Empty ? Guid.NewGuid() : p.Id; await c.ExecuteAsync("insert into ged.backup_policy(id,tenant_id,name,scope,enabled,backup_type,frequency,scheduled_at,timezone,retention_days,destination_kind,encryption_enabled,auto_verification_enabled,auto_restore_test_allowed,rpo_minutes,rto_minutes,status,created_by,change_justification) values (@id,@TenantId,@Name,@Scope,@Enabled,@BackupType,@Frequency,@ScheduledAt,@TimeZone,@RetentionDays,@DestinationKind,@EncryptionEnabled,@AutoVerificationEnabled,@AutoRestoreTestAllowed,@RpoMinutes,@RtoMinutes,@Status,@user,@just) on conflict(id) do update set name=excluded.name,enabled=excluded.enabled,frequency=excluded.frequency,retention_days=excluded.retention_days,updated_at_utc=now(),updated_by=@user,change_justification=@just", new { id, p.TenantId, p.Name, p.Scope, p.Enabled, p.BackupType, p.Frequency, p.ScheduledAt, p.TimeZone, p.RetentionDays, p.DestinationKind, p.EncryptionEnabled, p.AutoVerificationEnabled, p.AutoRestoreTestAllowed, p.RpoMinutes, p.RtoMinutes, p.Status, user=userName, just=justification }); return p with { Id = id }; }
    public async Task<IReadOnlyList<BackupSetDto>> ListAsync(Guid? tenantId, string? status, CancellationToken ct) { await using var c = await db.OpenAsync(ct); var rows = await c.QueryAsync<BackupSetDto>("select id, tenant_id TenantId, backup_type BackupType, started_at_utc StartedAtUtc, finished_at_utc FinishedAtUtc, status, size_bytes SizeBytes, file_count FileCount, integrity_status IntegrityStatus, location_masked LocationMasked, encryption_enabled EncryptionEnabled, manifest_checksum_sha256 ManifestChecksumSha256, correlation_id CorrelationId from ged.backup_set where (@tenant is null or tenant_id=@tenant) and (@status is null or status=@status) order by started_at_utc desc limit 200", new { tenant=tenantId, status }); return rows.AsList(); }
    public async Task<BackupSetDto?> GetAsync(Guid id, Guid? tenantId, CancellationToken ct) { await using var c = await db.OpenAsync(ct); return await c.QuerySingleOrDefaultAsync<BackupSetDto>("select id, tenant_id TenantId, backup_type BackupType, started_at_utc StartedAtUtc, finished_at_utc FinishedAtUtc, status, size_bytes SizeBytes, file_count FileCount, integrity_status IntegrityStatus, location_masked LocationMasked, encryption_enabled EncryptionEnabled, manifest_checksum_sha256 ManifestChecksumSha256, correlation_id CorrelationId from ged.backup_set where id=@id and (@tenant is null or tenant_id=@tenant)", new { id, tenant=tenantId }); }
    public async Task<ContinuityDashboardDto> GetDashboardAsync(Guid? tenantId, CancellationToken ct) { await using var c = await db.OpenAsync(ct); var last = await c.QuerySingleOrDefaultAsync<DateTime?>("select max(finished_at_utc) from ged.backup_set where status='COMPLETED' and (@tenant is null or tenant_id=@tenant)", new { tenant=tenantId }); var valid = await c.QuerySingleOrDefaultAsync<DateTime?>("select max(finished_at_utc) from ged.backup_set where integrity_status='VALID' and (@tenant is null or tenant_id=@tenant)", new { tenant=tenantId }); var failed = await c.QuerySingleAsync<int>("select count(*) from ged.backup_set where status='FAILED' and (@tenant is null or tenant_id=@tenant)", new { tenant=tenantId }); var dead = await c.QuerySingleAsync<int>("select count(*) from ged.operations_dead_letter where resolved_at_utc is null"); var active = await c.QuerySingleAsync<int>("select count(*) from ged.portability_export where status in ('REQUESTED','RUNNING','AVAILABLE') and (@tenant is null or tenant_id=@tenant)", new { tenant=tenantId }); var alerts = new List<string>(); if (!backupOptions.Value.Enabled) alerts.Add("Backup desabilitado por configuração."); if (last is null) alerts.Add("Nenhum backup concluído encontrado."); var rpo = valid.HasValue ? (int)(DateTime.UtcNow - valid.Value).TotalMinutes : null; return new(DateTime.UtcNow, alerts.Count == 0 ? "SAUDAVEL" : "NAO_CONFIGURADO", backupOptions.Value.Enabled, portabilityOptions.Value.Enabled, last, valid, null, null, valid.HasValue ? 100 : 0, failed, 0, backupOptions.Value.DefaultRetentionDays, null, rpo, null, null, 0, 0, active, 0, 0, dead, alerts); }
    public async Task<PortabilityExportDto> RequestAsync(Guid? tenantId, string scope, string requestedBy, string key, string correlationId, CancellationToken ct) { await using var c = await db.OpenAsync(ct); var existing = await c.QuerySingleOrDefaultAsync<PortabilityExportDto>("select id, tenant_id TenantId, scope, status, requested_at_utc RequestedAtUtc, finished_at_utc FinishedAtUtc, expires_at_utc ExpiresAtUtc, size_bytes SizeBytes, package_sha256 PackageSha256, correlation_id CorrelationId from ged.portability_export where tenant_id is not distinct from @tenant and idempotency_key=@key", new { tenant=tenantId, key }); if (existing is not null) return existing; var id=Guid.NewGuid(); await c.ExecuteAsync("insert into ged.portability_export(id,tenant_id,scope,status,requested_by,idempotency_key,correlation_id,expires_at_utc) values(@id,@tenant,@scope,'REQUESTED',@requestedBy,@key,@correlationId,now()+(@days||' days')::interval)", new { id, tenant=tenantId, scope, requestedBy, key, correlationId, days=portabilityOptions.Value.PackageExpirationDays }); return new(id, tenantId, scope, "REQUESTED", DateTime.UtcNow, null, DateTime.UtcNow.AddDays(portabilityOptions.Value.PackageExpirationDays), 0, null, correlationId); }
    public async Task<PortabilityExportDto?> GetAsync(Guid id, Guid? tenantId, CancellationToken ct) { await using var c = await db.OpenAsync(ct); return await c.QuerySingleOrDefaultAsync<PortabilityExportDto>("select id, tenant_id TenantId, scope, status, requested_at_utc RequestedAtUtc, finished_at_utc FinishedAtUtc, expires_at_utc ExpiresAtUtc, size_bytes SizeBytes, package_sha256 PackageSha256, correlation_id CorrelationId from ged.portability_export where id=@id and (@tenant is null or tenant_id=@tenant)", new { id, tenant=tenantId }); }
    public async Task<bool> CancelAsync(Guid id, Guid? tenantId, string requestedBy, CancellationToken ct) { await using var c = await db.OpenAsync(ct); return await c.ExecuteAsync("update ged.portability_export set status='CANCELLED', updated_at_utc=now() where id=@id and (@tenant is null or tenant_id=@tenant) and status in ('REQUESTED','RUNNING')", new { id, tenant=tenantId }) > 0; }
    public async Task<IReadOnlyList<RecoveryPlanDto>> ListPlansAsync(Guid? tenantId, CancellationToken ct) { await using var c = await db.OpenAsync(ct); var rows=await c.QueryAsync<RecoveryPlanDto>("select id, tenant_id TenantId, name, current_version CurrentVersion, status, rpo_minutes RpoMinutes, rto_minutes RtoMinutes, last_test_at_utc LastTestAtUtc, last_test_result LastTestResult, next_review_at_utc NextReviewAtUtc from ged.recovery_plan where (@tenant is null or tenant_id=@tenant or tenant_id is null)", new{tenant=tenantId}); return rows.AsList(); }
    public async Task<IReadOnlyList<TenantOffboardingDto>> ListOffboardingsAsync(Guid? tenantId, CancellationToken ct) { await using var c=await db.OpenAsync(ct); var rows=await c.QueryAsync<TenantOffboardingDto>("select id, tenant_id TenantId, status, started_at_utc StartedAtUtc, effective_at_utc EffectiveAtUtc, access_until_utc AccessUntilUtc, legal_hold LegalHold, justification from ged.tenant_offboarding where (@tenant is null or tenant_id=@tenant)", new{tenant=tenantId}); return rows.AsList(); }
    public async Task<TenantOffboardingDto> StartAsync(Guid tenantId, DateTime effectiveAtUtc, string justification, string requestedBy, CancellationToken ct) { await using var c=await db.OpenAsync(ct); var id=Guid.NewGuid(); var access=effectiveAtUtc.AddDays(90); await c.ExecuteAsync("insert into ged.tenant_offboarding(id,tenant_id,status,effective_at_utc,access_until_utc,justification,requested_by) values(@id,@tenantId,'DRAFT',@effectiveAtUtc,@access,@justification,@requestedBy)", new{id,tenantId,effectiveAtUtc,access,justification,requestedBy}); return new(id,tenantId,"DRAFT",DateTime.UtcNow,effectiveAtUtc,access,false,justification); }

    Task<IReadOnlyList<BackupPolicyDto>> IBackupPolicyService.ListAsync(Guid? tenantId, CancellationToken ct) => ListPoliciesAsync(tenantId, ct);
    Task<IReadOnlyList<RecoveryPlanDto>> IRecoveryPlanService.ListAsync(Guid? tenantId, CancellationToken ct) => ListPlansAsync(tenantId, ct);
    Task<IReadOnlyList<TenantOffboardingDto>> ITenantOffboardingService.ListAsync(Guid? tenantId, CancellationToken ct) => ListOffboardingsAsync(tenantId, ct);
    public async Task<RestoreValidationResult> ApproveDeletionAsync(Guid offboardingId, string justification, CancellationToken ct) { await using var c=await db.OpenAsync(ct); var blocked=await c.QuerySingleAsync<bool>("select coalesce((select legal_hold from ged.tenant_offboarding where id=@offboardingId), true)", new{offboardingId}); if(blocked) return new(false,"Legal hold ou registro inexistente bloqueia avanço."); return new(false,"Exclusão física automática está fora do escopo desta evolução."); }
    public async Task<bool> IsDeletionBlockedAsync(Guid tenantId, CancellationToken ct) { await using var c=await db.OpenAsync(ct); return await c.QuerySingleAsync<bool>("select exists(select 1 from ged.data_retention_hold where tenant_id=@tenantId and status='ACTIVE')", new{tenantId}); }
}

public sealed class BackupOrchestrator(IDbConnectionFactory db, IConfiguration configuration, IPostgresBackupProvider backupProvider, IBackupIntegrityService integrity, IOptions<BackupOptions> options) : IBackupOrchestrator
{
    private static readonly HashSet<string> TerminalStatuses = ["COMPLETED", "FAILED", "CANCELLED", "DEAD_LETTER"];

    public async Task<OperationJobDto> EnqueueBackupAsync(Guid? tenantId, Guid? policyId, string requestedBy, string correlationId, CancellationToken ct)
    {
        await using var c=await db.OpenAsync(ct); var id=Guid.NewGuid();
        await c.ExecuteAsync("insert into ged.backup_job(id,tenant_id,policy_id,job_type,status,requested_by,correlation_id,next_attempt_at_utc,attempts,max_attempts) values(@id,@tenantId,@policyId,'BACKUP','PENDING',@requestedBy,@correlationId,now(),0,3)", new{id,tenantId,policyId,requestedBy,correlationId});
        await AddJobEventAsync(c, id, null, "PENDING", requestedBy, "Backup solicitado", 0, correlationId, ct);
        return new(id,tenantId,"BACKUP","PENDING",0,"Aguardando worker",DateTime.UtcNow,null,correlationId);
    }

    public async Task<int> ProcessDueJobsAsync(string workerId, CancellationToken ct)
    {
        OperationJobDto? job;
        await using (var c=await db.OpenAsync(ct))
        await using (var tx=await c.BeginTransactionAsync(ct))
        {
            job = await c.QuerySingleOrDefaultAsync<OperationJobDto>(new CommandDefinition(@"
with claimed as (
  select id from ged.backup_job
  where status in ('PENDING','RETRY') and coalesce(next_attempt_at_utc, now()) <= now()
    and (locked_until_utc is null or locked_until_utc < now())
  order by created_at_utc
  for update skip locked limit 1
)
update ged.backup_job j set status='CLAIMED', worker_id=@workerId, locked_until_utc=now()+interval '15 minutes', current_step='CLAIMED'
from claimed where j.id=claimed.id
returning j.id, j.tenant_id TenantId, j.job_type JobType, j.status, coalesce(j.progress_percent,0) ProgressPercent, j.current_step CurrentStep, j.created_at_utc CreatedAtUtc, j.locked_until_utc LockedUntilUtc, j.correlation_id CorrelationId", new { workerId }, tx, cancellationToken: ct));
            if (job is null) { await tx.CommitAsync(ct); return 0; }
            await AddJobEventAsync(c, job.Id, "PENDING", "CLAIMED", workerId, "Job reivindicado com FOR UPDATE SKIP LOCKED", 5, job.CorrelationId, ct, tx);
            await tx.CommitAsync(ct);
        }

        try
        {
            ct.ThrowIfCancellationRequested();
            await UpdateJobAsync(job.Id, "RUNNING", workerId, "pg_dump em execução", 15, ct);
            var root = string.IsNullOrWhiteSpace(options.Value.RootPath) ? Path.Combine(Path.GetTempPath(), "inovaged-backups") : options.Value.RootPath;
            var backupSetId = Guid.NewGuid();
            var setDir = Path.Combine(root, job.TenantId?.ToString("N") ?? "global", backupSetId.ToString("N"));
            Directory.CreateDirectory(setDir);
            var dumpPath = Path.Combine(setDir, "database.dump");
            var cs = configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection não configurada.");
            await using (var c=await db.OpenAsync(ct))
            {
                await c.ExecuteAsync(new CommandDefinition("insert into ged.backup_set(id,tenant_id,backup_type,started_at_utc,status,integrity_status,location_masked,location_internal,correlation_id) values(@id,@tenant,'POSTGRESQL',now(),'RUNNING','PENDING',@masked,@internal,@correlation)", new{id=backupSetId,tenant=job.TenantId,masked=$"backup://{backupSetId:N}",internal=setDir,correlation=job.CorrelationId}, cancellationToken:ct));
            }
            var result = await backupProvider.DumpAsync(cs, dumpPath, ct);
            if (!result.Success) throw new InvalidOperationException($"pg_dump falhou: {result.SanitizedError}");
            var manifestPath = Path.Combine(setDir, "manifest.json");
            var manifest = new { format="inovaged-backup-manifest", backupSetId, tenantId=job.TenantId, startedAtUtc=DateTime.UtcNow, completedAtUtc=DateTime.UtcNow, application="InovaGED", schema="ged", postgresql="server", pgDump=result.PgDumpVersion, type="POSTGRESQL", consistency="pg_restore-list", artifacts=new[]{new{path="database.dump", sha256=result.Sha256, sizeBytes=new FileInfo(dumpPath).Length}}, status="COMPLETED", correlationId=job.CorrelationId };
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions{WriteIndented=true}), ct);
            var manifestHash = await Sha256Async(manifestPath, ct);
            await File.WriteAllTextAsync(Path.Combine(setDir, "checksums.sha256"), $"{result.Sha256}  database.dump\n{manifestHash}  manifest.json\n", ct);
            var total = Directory.EnumerateFiles(setDir).Sum(f => new FileInfo(f).Length);
            await using (var c=await db.OpenAsync(ct))
            {
                await c.ExecuteAsync(new CommandDefinition("update ged.backup_set set status='COMPLETED', finished_at_utc=now(), size_bytes=@total, file_count=3, manifest_checksum_sha256=@manifestHash where id=@backupSetId", new{backupSetId,total,manifestHash}, cancellationToken:ct));
            }
            if (options.Value.VerificationEnabled) await integrity.VerifyAsync(backupSetId, workerId, ct);
            await UpdateJobAsync(job.Id, "COMPLETED", workerId, "Artefato, manifesto e checksum concluídos", 100, ct);
            return 1;
        }
        catch (OperationCanceledException)
        {
            await UpdateJobAsync(job.Id, "RETRY", workerId, "Cancelamento solicitado durante processamento", 0, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            await FailOrRetryAsync(job.Id, workerId, ex.Message, ct);
            return 1;
        }
    }

    private async Task UpdateJobAsync(Guid id,string status,string worker,string step,int progress,CancellationToken ct)
    { await using var c=await db.OpenAsync(ct); await c.ExecuteAsync(new CommandDefinition("update ged.backup_job set status=@status, worker_id=@worker, current_step=@step, progress_percent=@progress, locked_until_utc=case when @status = any(array['COMPLETED','FAILED','DEAD_LETTER','CANCELLED']) then null else now()+interval '15 minutes' end, finished_at_utc=case when @status = any(array['COMPLETED','FAILED','DEAD_LETTER','CANCELLED']) then now() else finished_at_utc end where id=@id and status <> all(@terminal)", new{id,status,worker,step,progress,terminal=TerminalStatuses.ToArray()}, cancellationToken:ct)); await AddJobEventAsync(c,id,null,status,worker,step,progress,null,ct); }
    private async Task FailOrRetryAsync(Guid id,string worker,string reason,CancellationToken ct){ await using var c=await db.OpenAsync(ct); await c.ExecuteAsync(new CommandDefinition("update ged.backup_job set attempts=attempts+1, status=case when attempts+1 >= max_attempts then 'DEAD_LETTER' else 'RETRY' end, current_step=@reason, locked_until_utc=null, next_attempt_at_utc=now()+interval '5 minutes' where id=@id", new{id,reason}, cancellationToken:ct)); await AddJobEventAsync(c,id,null,"RETRY",worker,reason,0,null,ct); }
    private static Task AddJobEventAsync(System.Data.Common.DbConnection c, Guid id, string? oldStatus, string newStatus, string worker, string reason, int progress, string? correlation, CancellationToken ct, System.Data.Common.DbTransaction? tx=null) => c.ExecuteAsync(new CommandDefinition("insert into ged.operation_job_event(job_id,old_status,new_status,worker_id,reason,progress_percent,correlation_id) values(@id,@oldStatus,@newStatus,@worker,@reason,@progress,@correlation)", new{id,oldStatus,newStatus,worker,reason,progress,correlation}, tx, cancellationToken:ct));
    private static async Task<string> Sha256Async(string path,CancellationToken ct){ await using var fs=File.OpenRead(path); return Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(fs,ct)).ToLowerInvariant(); }
}
public sealed class BackupIntegrityService(IDbConnectionFactory db) : IBackupIntegrityService { public async Task<BackupVerificationResult> VerifyAsync(Guid backupSetId, string workerId, CancellationToken ct){ await using var c=await db.OpenAsync(ct); var set=await c.QuerySingleOrDefaultAsync<(string? Path,long Size)>("select coalesce(location_internal, location_masked), size_bytes from ged.backup_set where id=@backupSetId", new{backupSetId}); var findings=new List<string>(); var status="NOT_VERIFIABLE"; if(string.IsNullOrWhiteSpace(set.Path)) findings.Add("Localização não disponível para verificação pelo nó atual."); else if(Directory.Exists(set.Path)) { var manifest=Path.Combine(set.Path,"manifest.json"); var checksums=Path.Combine(set.Path,"checksums.sha256"); var dump=Path.Combine(set.Path,"database.dump"); if(!File.Exists(manifest)) findings.Add("manifest.json ausente."); if(!File.Exists(checksums)) findings.Add("checksums.sha256 ausente."); if(!File.Exists(dump)) findings.Add("database.dump ausente."); if(Directory.EnumerateFiles(set.Path,"*.partial").Any()) findings.Add("Arquivo parcial encontrado."); status=findings.Count==0?"VALID":"INVALID"; } else findings.Add("Diretório interno não encontrado."); await c.ExecuteAsync("insert into ged.backup_verification(backup_set_id,status,findings_json,worker_id) values(@backupSetId,@status,@json::jsonb,@workerId)", new{backupSetId,status,json=JsonSerializer.Serialize(findings),workerId}); return new(backupSetId,status,findings,DateTime.UtcNow); } }
public sealed class RestoreValidationService(IConfiguration cfg) : IRestoreValidationService { public Task<RestoreValidationResult> ValidateTargetAsync(string host,string database,bool confirmed,string justification,CancellationToken ct){ var prod=cfg.GetConnectionString("DefaultConnection")??string.Empty; var prodBuilder=string.IsNullOrWhiteSpace(prod)?null:new Npgsql.NpgsqlConnectionStringBuilder(prod); var allow=cfg.GetSection("RestoreTest:AllowedDatabases").Get<string[]>()??[]; if(!confirmed || string.IsNullOrWhiteSpace(justification)) return Task.FromResult(new RestoreValidationResult(false,"Confirmação e justificativa são obrigatórias.")); if(prodBuilder is not null && string.Equals(prodBuilder.Host, host, StringComparison.OrdinalIgnoreCase) && string.Equals(prodBuilder.Database, database, StringComparison.OrdinalIgnoreCase)) return Task.FromResult(new RestoreValidationResult(false,"Destino de produção bloqueado por comparação exata.")); return Task.FromResult(allow.Contains(database,StringComparer.OrdinalIgnoreCase)?new RestoreValidationResult(true,"Destino autorizado para teste isolado."):new RestoreValidationResult(false,"Destino fora da allowlist.")); } }
public sealed class PortabilityManifestService(IDbConnectionFactory db) : IPortabilityManifestService { public async Task<PortabilityManifest> BuildAsync(Guid exportId, CancellationToken ct){ await using var c=await db.OpenAsync(ct); var row=await c.QuerySingleOrDefaultAsync<(Guid? TenantId,string Scope,string Status,string? CorrelationId)>("select tenant_id, scope, status, correlation_id from ged.portability_export where id=@exportId", new{exportId}); if(row==default) return new("1.0",exportId,null,"UNKNOWN",DateTime.UtcNow,"NOT_FOUND",Guid.NewGuid().ToString("N"),[]); var files=(await c.QueryAsync<PortabilityManifestFile>(new CommandDefinition("select relative_path Path, size_bytes SizeBytes, sha256 Sha256 from ged.portability_artifact where export_id=@exportId order by relative_path", new{exportId}, cancellationToken:ct))).ToList(); return new("1.0",exportId,row.TenantId,row.Scope,DateTime.UtcNow,row.Status,row.CorrelationId??Guid.NewGuid().ToString("N"),files); } }
public sealed class PortabilityPackageVerifier : IPortabilityPackageVerifier { public async Task<PortabilityVerificationResult> VerifyAsync(string packagePath, CancellationToken ct){ if(!Directory.Exists(packagePath)) return new(false,["Pacote não encontrado."]); var manifest=Path.Combine(packagePath,"manifest.json"); if(!File.Exists(manifest)) return new(false,["manifest.json ausente."]); var findings=new List<string>(); var json=await File.ReadAllTextAsync(manifest,ct); if(json.Contains("password",StringComparison.OrdinalIgnoreCase)||json.Contains("token",StringComparison.OrdinalIgnoreCase)) findings.Add("Manifesto contém termo sensível bloqueado."); return new(findings.Count==0, findings); } }
