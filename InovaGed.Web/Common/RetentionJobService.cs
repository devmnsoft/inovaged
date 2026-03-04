using Dapper;
using InovaGed.Application.Common.Database;

public sealed class RetentionJobService
{
    private readonly IDbConnectionFactory _db;
    public RetentionJobService(IDbConnectionFactory db) => _db = db;

    public async Task RunAsync(Guid tenantId, Guid? actorUserId, CancellationToken ct)
    {
        using var conn = await _db.OpenAsync(ct);
        using var tx = conn.BeginTransaction();

        // 1) Limpa fila anterior (opcional: manter histórico)
        await conn.ExecuteAsync(@"
update ged.retention_queue
set reg_status='I'
where tenant_id=@tenantId and reg_status='A';", new { tenantId }, tx);

        // 2) Recalcula e enfileira (vencidos e a vencer)
        // Premissa do teu dump: existe document_retention, retention_rule e retention_queue
        // document_retention tem computed_due_at / final_destination etc.
        await conn.ExecuteAsync(@"
insert into ged.retention_queue
(id, tenant_id, document_id, due_at, bucket, created_at, created_by, reg_date, reg_status)
select
  gen_random_uuid(), dr.tenant_id, dr.document_id, dr.due_at,
  case
    when dr.due_at <= now() then 'EXPIRED'
    when dr.due_at <= now() + interval '30 days' then 'DUE_30'
    when dr.due_at <= now() + interval '60 days' then 'DUE_60'
    when dr.due_at <= now() + interval '90 days' then 'DUE_90'
    else 'OK'
  end as bucket,
  now(), @actorUserId, now(), 'A'
from ged.document_retention dr
where dr.tenant_id=@tenantId
  and dr.reg_status='A'
  and dr.due_at is not null;", new { tenantId, actorUserId }, tx);

        // 3) Auditoria (se teu audit_log já existe, registre ação RETENTION_QUEUE_GENERATE)
        await conn.ExecuteAsync(@"
insert into ged.audit_log
(id, tenant_id, event_time, user_id, action, entity_name, entity_id, summary, event_type, is_success, reg_date, reg_status)
values
(gen_random_uuid(), @tenantId, now(), @actorUserId, 'RETENTION_QUEUE_GENERATE', 'retention_queue', null,
 'Job de temporalidade executado: fila recalculada', 'INFO', true, now(), 'A');", new { tenantId, actorUserId }, tx);

        tx.Commit();
    }
}