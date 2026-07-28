using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Continuity;

namespace InovaGed.Infrastructure.Continuity;

public sealed class BackupCatalogRepository(IDbConnectionFactory db) : IBackupCatalogService
{
    private const string Projection = "select id, tenant_id TenantId, backup_type BackupType, started_at_utc StartedAtUtc, finished_at_utc FinishedAtUtc, status, size_bytes SizeBytes, file_count FileCount, integrity_status IntegrityStatus, location_masked LocationMasked, encryption_enabled EncryptionEnabled, manifest_checksum_sha256 ManifestChecksumSha256, correlation_id CorrelationId from ged.backup_set";
    public async Task<IReadOnlyList<BackupSetDto>> ListAsync(Guid? tenantId, string? status, CancellationToken ct) { await using var c = await db.OpenAsync(ct); var rows = await c.QueryAsync<BackupSetDto>(new CommandDefinition(Projection + " where (@tenantId is null or tenant_id=@tenantId) and (@status is null or status=@status) order by started_at_utc desc limit 200", new { tenantId, status }, cancellationToken: ct)); return rows.AsList(); }
    public async Task<BackupSetDto?> GetAsync(Guid id, Guid? tenantId, CancellationToken ct) { await using var c = await db.OpenAsync(ct); return await c.QuerySingleOrDefaultAsync<BackupSetDto>(new CommandDefinition(Projection + " where id=@id and (@tenantId is null or tenant_id=@tenantId)", new { id, tenantId }, cancellationToken: ct)); }
}
