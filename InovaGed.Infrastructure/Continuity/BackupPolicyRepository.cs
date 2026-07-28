using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Continuity;

namespace InovaGed.Infrastructure.Continuity;

public sealed class BackupPolicyRepository(IDbConnectionFactory db) : IBackupPolicyService
{
    public async Task<IReadOnlyList<BackupPolicyDto>> ListAsync(Guid? tenantId, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        var rows = await connection.QueryAsync<BackupPolicyDto>(new CommandDefinition("select id, tenant_id TenantId, name, scope, enabled, backup_type BackupType, frequency, scheduled_at ScheduledAt, timezone TimeZone, retention_days RetentionDays, destination_kind DestinationKind, encryption_enabled EncryptionEnabled, auto_verification_enabled AutoVerificationEnabled, auto_restore_test_allowed AutoRestoreTestAllowed, rpo_minutes RpoMinutes, rto_minutes RtoMinutes, status, created_at_utc CreatedAtUtc, updated_at_utc UpdatedAtUtc from ged.backup_policy where (@tenantId is null or tenant_id=@tenantId or tenant_id is null) order by created_at_utc desc", new { tenantId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<BackupPolicyDto> SaveAsync(BackupPolicyDto policy, string userName, string justification, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName); ArgumentException.ThrowIfNullOrWhiteSpace(justification);
        await using var connection = await db.OpenAsync(ct);
        var id = policy.Id == Guid.Empty ? Guid.NewGuid() : policy.Id;
        const string sql = "insert into ged.backup_policy(id,tenant_id,name,scope,enabled,backup_type,frequency,scheduled_at,timezone,retention_days,destination_kind,encryption_enabled,auto_verification_enabled,auto_restore_test_allowed,rpo_minutes,rto_minutes,status,created_by,change_justification) values (@id,@TenantId,@Name,@Scope,@Enabled,@BackupType,@Frequency,@ScheduledAt,@TimeZone,@RetentionDays,@DestinationKind,@EncryptionEnabled,@AutoVerificationEnabled,@AutoRestoreTestAllowed,@RpoMinutes,@RtoMinutes,@Status,@userName,@justification) on conflict(id) do update set name=excluded.name,enabled=excluded.enabled,frequency=excluded.frequency,retention_days=excluded.retention_days,updated_at_utc=now(),updated_by=@userName,change_justification=@justification";
        await connection.ExecuteAsync(new CommandDefinition(sql, new { id, policy.TenantId, policy.Name, policy.Scope, policy.Enabled, policy.BackupType, policy.Frequency, policy.ScheduledAt, policy.TimeZone, policy.RetentionDays, policy.DestinationKind, policy.EncryptionEnabled, policy.AutoVerificationEnabled, policy.AutoRestoreTestAllowed, policy.RpoMinutes, policy.RtoMinutes, policy.Status, userName, justification }, cancellationToken: ct));
        return policy with { Id = id };
    }
}
