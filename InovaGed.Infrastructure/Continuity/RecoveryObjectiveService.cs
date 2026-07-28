using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Continuity;
using Microsoft.Extensions.Options;

namespace InovaGed.Infrastructure.Continuity;

public sealed class RecoveryObjectiveService(IDbConnectionFactory db, IOptions<BackupOptions> backup, IOptions<PortabilityOptions> portability) : IRecoveryObjectiveService
{
    internal static int? CalculateObservedRpoMinutes(DateTime? validBackupUtc, DateTime nowUtc)
    {
        if (!validBackupUtc.HasValue) return null;
        var minutes = Math.Max(0d, (nowUtc.ToUniversalTime() - validBackupUtc.Value.ToUniversalTime()).TotalMinutes);
        return checked((int)Math.Min(int.MaxValue, minutes));
    }

    public async Task<ContinuityDashboardDto> GetDashboardAsync(Guid? tenantId, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        var last = await c.QuerySingleOrDefaultAsync<DateTime?>(new CommandDefinition("select max(finished_at_utc) from ged.backup_set where status='COMPLETED' and (@tenantId is null or tenant_id=@tenantId)", new { tenantId }, cancellationToken: ct));
        var valid = await c.QuerySingleOrDefaultAsync<DateTime?>(new CommandDefinition("select max(finished_at_utc) from ged.backup_set where integrity_status='VALID' and (@tenantId is null or tenant_id=@tenantId)", new { tenantId }, cancellationToken: ct));
        var metrics = await c.QuerySingleAsync<(int Failed, long Bytes, int Verified, int Valid, int Running)>(new CommandDefinition("select count(*) filter(where status='FAILED')::int Failed, coalesce(sum(size_bytes),0)::bigint Bytes, count(*) filter(where integrity_status in ('VALID','INVALID'))::int Verified, count(*) filter(where integrity_status='VALID')::int Valid, count(*) filter(where status='RUNNING')::int Running from ged.backup_set where (@tenantId is null or tenant_id=@tenantId)", new { tenantId }, cancellationToken: ct));
        var jobs = await c.QuerySingleAsync<(int Dead, int Retry)>(new CommandDefinition("select count(*) filter(where status='DEAD_LETTER')::int Dead, count(*) filter(where status='RETRY')::int Retry from ged.backup_job where (@tenantId is null or tenant_id=@tenantId)", new { tenantId }, cancellationToken: ct));
        var active = await c.QuerySingleAsync<int>(new CommandDefinition("select count(*)::int from ged.portability_export where status in ('REQUESTED','CLAIMED','RUNNING','VERIFYING','AVAILABLE') and (@tenantId is null or tenant_id=@tenantId)", new { tenantId }, cancellationToken: ct));
        var alerts = new List<string>(); if (!backup.Value.Enabled) alerts.Add("Backup desabilitado por configuração."); if (valid is null) alerts.Add("Nenhum backup válido encontrado."); if (jobs.Dead > 0) alerts.Add("Existem jobs em dead letter.");
        int? observedRpoMinutes = CalculateObservedRpoMinutes(valid, DateTime.UtcNow);
        var integrity = metrics.Verified == 0 ? 0 : decimal.Round(100m * metrics.Valid / metrics.Verified, 2);
        var status = !backup.Value.Enabled ? "NAO_CONFIGURADO" : valid is null ? "CRITICO" : jobs.Dead > 0 || jobs.Retry > 0 ? "DEGRADADO" : "SAUDAVEL";
        return new(DateTime.UtcNow,status,backup.Value.Enabled,portability.Value.Enabled,last,valid,null,null,integrity,metrics.Failed,metrics.Bytes,backup.Value.DefaultRetentionDays,null,observedRpoMinutes,null,null,0,0,active,0,metrics.Running,jobs.Dead,alerts);
    }
}
