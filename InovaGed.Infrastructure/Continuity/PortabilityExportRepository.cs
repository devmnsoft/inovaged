using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Continuity;
using Microsoft.Extensions.Options;

namespace InovaGed.Infrastructure.Continuity;

public sealed class PortabilityExportRepository(IDbConnectionFactory db, IOptions<PortabilityOptions> options) : IPortabilityExportService
{
    private const string Projection = "select id, tenant_id TenantId, scope, status, requested_at_utc RequestedAtUtc, finished_at_utc FinishedAtUtc, expires_at_utc ExpiresAtUtc, size_bytes SizeBytes, package_sha256 PackageSha256, correlation_id CorrelationId from ged.portability_export";
    public async Task<PortabilityExportDto> RequestAsync(Guid? tenantId, string scope, string requestedBy, string idempotencyKey, string correlationId, CancellationToken ct) { ArgumentException.ThrowIfNullOrWhiteSpace(scope); ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey); await using var c=await db.OpenAsync(ct); var existing=await c.QuerySingleOrDefaultAsync<PortabilityExportDto>(new CommandDefinition(Projection+" where tenant_id is not distinct from @tenantId and idempotency_key=@idempotencyKey",new{tenantId,idempotencyKey},cancellationToken:ct)); if(existing is not null)return existing; var id=Guid.NewGuid(); await c.ExecuteAsync(new CommandDefinition("insert into ged.portability_export(id,tenant_id,scope,status,requested_by,idempotency_key,correlation_id,expires_at_utc) values(@id,@tenantId,@scope,'REQUESTED',@requestedBy,@idempotencyKey,@correlationId,now()+(@days||' days')::interval)",new{id,tenantId,scope,requestedBy,idempotencyKey,correlationId,days=options.Value.PackageExpirationDays},cancellationToken:ct)); return new(id,tenantId,scope,"REQUESTED",DateTime.UtcNow,null,DateTime.UtcNow.AddDays(options.Value.PackageExpirationDays),0,null,correlationId); }
    public async Task<PortabilityExportDto?> GetAsync(Guid id, Guid? tenantId, CancellationToken ct) { await using var c=await db.OpenAsync(ct); return await c.QuerySingleOrDefaultAsync<PortabilityExportDto>(new CommandDefinition(Projection+" where id=@id and (@tenantId is null or tenant_id=@tenantId) and (expires_at_utc is null or expires_at_utc > now())",new{id,tenantId},cancellationToken:ct)); }
    public async Task<bool> CancelAsync(Guid id, Guid? tenantId, string requestedBy, CancellationToken ct) { await using var c=await db.OpenAsync(ct); return await c.ExecuteAsync(new CommandDefinition("update ged.portability_export set status='CANCELLED', updated_at_utc=now() where id=@id and (@tenantId is null or tenant_id=@tenantId) and status in ('REQUESTED','CLAIMED','RUNNING','VERIFYING')",new{id,tenantId},cancellationToken:ct))>0; }
}
