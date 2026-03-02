using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.RetentionCases;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.RetentionCases;

public sealed class RetentionCaseRepository : IRetentionCaseRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<RetentionCaseRepository> _logger;

    public RetentionCaseRepository(IDbConnectionFactory db, ILogger<RetentionCaseRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RetentionCaseRow>> ListAsync(Guid tenantId, string? status, CancellationToken ct)
    {
        var sql = @"
select
  c.id        as ""Id"",
  c.case_no   as ""CaseNo"",
  c.title     as ""Title"",
  c.status    as ""Status"",
  c.created_at as ""CreatedAt""
from ged.retention_case c
where c.tenant_id = @tenantId
";
        var p = new DynamicParameters();
        p.Add("tenantId", tenantId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            sql += " and status=@status ";
            p.Add("status", status);
        }

        sql += " order by created_at desc limit 200;";

        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<RetentionCaseRow>(sql, p);
        return rows.ToList();
    }

    public async Task<(RetentionCaseRow Case, IReadOnlyList<RetentionCaseItemRow> Items)?> GetAsync(Guid tenantId, Guid caseId, CancellationToken ct)
    {
        const string sqlCase = @"
select id as Id, case_no as CaseNo, title as Title, status as Status, created_at as CreatedAt
from ged.retention_case
where tenant_id=@tenantId and id=@caseId
limit 1;
";

        const string sqlItems = @"
select
  i.id as Id,
  i.case_id as CaseId,
  i.document_id as DocumentId,
  i.doc_code as DocCode,
  i.doc_title as DocTitle,
  i.classification_code as ClassificationCode,
  i.classification_name as ClassificationName,
  i.retention_due_at as RetentionDueAt,
  i.retention_status as RetentionStatus,
  i.suggested_destination as SuggestedDestination,
  i.decision as Decision,
  i.decision_notes as DecisionNotes,
  i.decided_at as DecidedAt
from ged.retention_case_item i
where i.tenant_id=@tenantId and i.case_id=@caseId
order by i.retention_due_at nulls last, i.doc_title;
";

        await using var conn = await _db.OpenAsync(ct);
        var c = await conn.QueryFirstOrDefaultAsync<RetentionCaseRow>(sqlCase, new { tenantId, caseId });
        if (c is null) return null;

        var items = await conn.QueryAsync<RetentionCaseItemRow>(sqlItems, new { tenantId, caseId });
        return (c, items.ToList());
    }

    public async Task<Guid> CreateAsync(Guid tenantId, Guid userId, CreateRetentionCaseRequest req, CancellationToken ct)
    {
        if (req.DocumentIds.Length == 0)
            throw new ArgumentException("Selecione ao menos 1 documento.");

        var caseId = Guid.NewGuid();

        const string sqlNextNo = @"select coalesce(max(case_no),0)+1 from ged.retention_case where tenant_id=@tenantId;";

        const string sqlInsertCase = @"
insert into ged.retention_case(id, tenant_id, case_no, title, status, created_at, created_by, notes)
values (@id, @tenantId, @caseNo, @title, 'OPEN', now(), @userId, @notes);
";

        // Snapshot dos docs (usa dados do ged.document + classification_plan)
        const string sqlInsertItems = @"
insert into ged.retention_case_item(
  tenant_id, case_id, document_id,
  doc_code, doc_title,
  classification_id, classification_code, classification_name,
  classification_version_id,
  retention_due_at, retention_status,
  suggested_destination
)
select
  d.tenant_id,
  @caseId,
  d.id,
  d.code,
  d.title,
  d.classification_id,
  c.code,
  c.name,
  d.classification_version_id,
  d.retention_due_at,
  d.retention_status,
  c.final_destination
from ged.document d
left join ged.classification_plan c
  on c.tenant_id=d.tenant_id and c.id=d.classification_id
where d.tenant_id=@tenantId
  and d.id = any(@docIds)
  and coalesce(d.retention_hold,false) = false; -- bloqueio por HOLD
";

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var tx = conn.BeginTransaction();

            var caseNo = await conn.ExecuteScalarAsync<int>(sqlNextNo, new { tenantId }, tx);

            await conn.ExecuteAsync(sqlInsertCase, new
            {
                id = caseId,
                tenantId,
                caseNo,
                title = (req.Title ?? "Caso de Destinação").Trim(),
                notes = req.Notes,
                userId
            }, tx);

            var inserted = await conn.ExecuteAsync(sqlInsertItems, new
            {
                tenantId,
                caseId,
                docIds = req.DocumentIds
            }, tx);

            if (inserted == 0)
                throw new InvalidOperationException("Nenhum documento foi inserido no caso (pode estar em HOLD ou inexistente).");

            await tx.CommitAsync(ct);
            return caseId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Create retention case failed. Tenant={TenantId}", tenantId);
            throw;
        }
    }

    public async Task DecideItemAsync(Guid tenantId, Guid userId, DecideItemRequest req, CancellationToken ct)
    {
        if (req.Decision is not ("APPROVE" or "REJECT"))
            throw new ArgumentException("Decision inválida. Use APPROVE ou REJECT.");

        const string sql = @"
update ged.retention_case_item
set
  decision = @decision,
  decision_notes = @notes,
  decided_at = now(),
  decided_by = @userId
where tenant_id=@tenantId and id=@itemId;
";

        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.ExecuteAsync(sql, new { tenantId, userId, itemId = req.ItemId, decision = req.Decision, notes = req.Notes });
        if (rows == 0) throw new InvalidOperationException("Item não encontrado.");
    }

    public async Task CloseCaseAsync(Guid tenantId, Guid userId, Guid caseId, string newStatus, CancellationToken ct)
    {
        if (newStatus is not ("APPROVED" or "REJECTED" or "EXECUTED" or "CANCELED"))
            throw new ArgumentException("Status inválido.");

        const string sql = @"
update ged.retention_case
set status=@status,
    closed_at = now(),
    closed_by = @userId
where tenant_id=@tenantId and id=@caseId;
";

        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.ExecuteAsync(sql, new { tenantId, userId, caseId, status = newStatus });
        if (rows == 0) throw new InvalidOperationException("Caso não encontrado.");
    }

    public async Task<Guid> CreateFromQueueAsync(
       Guid tenantId,
       Guid? userId,
       string? userDisplay,
       Guid[] queueIds,
       string? title,
       string? notes,
       CancellationToken ct)
    {
        if (queueIds == null || queueIds.Length == 0)
            throw new ArgumentException("Selecione ao menos 1 item da fila.", nameof(queueIds));

        var ids = queueIds.Distinct().ToArray();
        var caseId = Guid.NewGuid();

        const string sqlNextNo = @"select coalesce(max(case_no),0)+1 from ged.retention_case where tenant_id=@tenantId;";

        const string sqlInsertCase = @"
insert into ged.retention_case(id, tenant_id, case_no, title, status, created_at, created_by, notes)
values (@id, @tenantId, @caseNo, @title, 'OPEN', now(), @userId, @notes);
";

        // ✅ IMPORTANTÍSSIMO: inserir itens no mesmo formato do seu schema real (doc_code, doc_title, classification_*, retention_*)
        // Pega document_id da fila e faz snapshot do documento/classificação.
        const string sqlInsertItemsFromQueue = @"
insert into ged.retention_case_item(
  tenant_id, case_id, document_id,
  doc_code, doc_title,
  classification_id, classification_code, classification_name,
  classification_version_id,
  retention_due_at, retention_status,
  suggested_destination
)
select
  d.tenant_id,
  @caseId,
  d.id,
  d.code,
  d.title,
  d.classification_id,
  c.code,
  c.name,
  d.classification_version_id,
  d.retention_due_at,
  d.retention_status,
  c.final_destination
from ged.retention_queue q
join ged.document d
  on d.tenant_id=q.tenant_id and d.id=q.document_id
left join ged.classification_plan c
  on c.tenant_id=d.tenant_id and c.id=d.classification_id
where q.tenant_id=@tenantId
  and q.id = any(@queueIds)
  and coalesce(d.retention_hold,false) = false;
";

        // ✅ FIX: removeu o texto “GetAsync” que estava quebrando seu SQL
        const string sqlMarkQueueInTerm = @"
update ged.retention_queue
set status='IN_TERM',
    reg_date=now()
where tenant_id=@tenantId
  and reg_status='A'
  and id = any(@queueIds);
";

        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var tx = conn.BeginTransaction();

            var caseNo = await conn.ExecuteScalarAsync<int>(
                new CommandDefinition(sqlNextNo, new { tenantId }, transaction: tx, cancellationToken: ct));

            await conn.ExecuteAsync(new CommandDefinition(sqlInsertCase, new
            {
                id = caseId,
                tenantId,
                caseNo,
                title = string.IsNullOrWhiteSpace(title) ? "Caso de Destinação" : title.Trim(),
                notes,
                userId
            }, transaction: tx, cancellationToken: ct));

            var inserted = await conn.ExecuteAsync(new CommandDefinition(sqlInsertItemsFromQueue, new
            {
                tenantId,
                caseId,
                queueIds = ids
            }, transaction: tx, cancellationToken: ct));

            if (inserted == 0)
                throw new InvalidOperationException("Nenhum documento foi inserido no caso (pode estar em HOLD ou inexistente).");

            await conn.ExecuteAsync(new CommandDefinition(sqlMarkQueueInTerm, new
            {
                tenantId,
                queueIds = ids
            }, transaction: tx, cancellationToken: ct));

            await tx.CommitAsync(ct);

            _logger.LogInformation(
                "RetentionCase criado via Queue. Tenant={Tenant} CaseId={CaseId} CaseNo={CaseNo} Items={Count} User={User}",
                tenantId, caseId, caseNo, inserted, userDisplay);

            return caseId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateFromQueueAsync failed Tenant={Tenant} QueueItems={Count}", tenantId, ids.Length);
            throw;
        }
    }
}