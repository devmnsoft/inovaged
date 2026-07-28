using Dapper; using InovaGed.Application.Common.Database; using InovaGed.Application.Continuity;
namespace InovaGed.Infrastructure.Continuity;
public sealed class DataDeletionWorkflowService(IDbConnectionFactory db):IDataDeletionWorkflowService { public async Task<bool> IsDeletionBlockedAsync(Guid tenantId,CancellationToken ct){await using var c=await db.OpenAsync(ct);return await c.QuerySingleAsync<bool>(new CommandDefinition("select exists(select 1 from ged.data_retention_hold where tenant_id=@tenantId and status='ACTIVE')",new{tenantId},cancellationToken:ct));} }
