using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Loans;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged.Loans;

public sealed class LoanQueries : ILoanQueries
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<LoanQueries> _logger;

    public LoanQueries(IDbConnectionFactory db, ILogger<LoanQueries> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<LoanRowDto>> ListAsync(Guid tenantId, string? q, string? status, CancellationToken ct)
    {
        try
        {
            await using var con = await _db.OpenAsync(ct);

            q ??= "";
            var qTrim = q.Trim();

            long? protocolNo = null;
            if (!string.IsNullOrWhiteSpace(qTrim) && long.TryParse(qTrim, out var p))
                protocolNo = p;

            const string sql = """
select
    l.id,
    l.protocol_no                 as ProtocolNo,
    l.status::text                as Status,
    coalesce(l.requester_name,'') as RequesterName,
    l.requested_at                as RequestedAt,
    l.due_at                      as DueAt,
    l.approved_at                 as ApprovedAt,
    l.delivered_at                as DeliveredAt,
    l.returned_at                 as ReturnedAt,
    (
        select count(*)::int
        from ged.loan_request_item i
        where i.tenant_id = l.tenant_id
          and i.loan_request_id = l.id
          and i.reg_status='A'
    )                             as ItemsCount
from ged.loan_request l
where l.tenant_id = @TenantId
  and l.reg_status = 'A'
  and (@Status is null or @Status = '' or l.status::text = @Status)
  and (
        @Q is null or @Q = ''
        or coalesce(l.requester_name,'') ilike ('%' || @Q || '%')
        or l.status::text ilike ('%' || @Q || '%')
        or l.protocol_no::text ilike ('%' || @Q || '%')
        or (@ProtocolNo is not null and l.protocol_no = @ProtocolNo)
  )
order by l.requested_at desc
limit 200;
""";

            var rows = await con.QueryAsync<LoanRowDto>(new CommandDefinition(
                sql,
                new { TenantId = tenantId, Q = qTrim, Status = status, ProtocolNo = protocolNo },
                cancellationToken: ct));

            return rows.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LoanQueries.ListAsync failed. Tenant={Tenant}", tenantId);
            throw;
        }
    }

    public async Task<IReadOnlyList<LoanRowDto>> ListOverdueAsync(Guid tenantId, CancellationToken ct)
    {
        try
        {
            await using var con = await _db.OpenAsync(ct);

            const string sql = """
select
    l.id,
    l.protocol_no                 as ProtocolNo,
    l.status::text                as Status,
    coalesce(l.requester_name,'') as RequesterName,
    l.requested_at                as RequestedAt,
    l.due_at                      as DueAt,
    l.approved_at                 as ApprovedAt,
    l.delivered_at                as DeliveredAt,
    l.returned_at                 as ReturnedAt,
    (
        select count(*)::int
        from ged.loan_request_item i
        where i.tenant_id = l.tenant_id
          and i.loan_request_id = l.id
          and i.reg_status='A'
    )                             as ItemsCount
from ged.loan_request l
where l.tenant_id = @TenantId
  and l.reg_status='A'
  and l.due_at < now()
  and l.returned_at is null
order by l.due_at asc
limit 200;
""";

            var rows = await con.QueryAsync<LoanRowDto>(
                new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: ct));

            return rows.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LoanQueries.ListOverdueAsync failed. Tenant={Tenant}", tenantId);
            throw;
        }
    }

    public async Task<LoanDetailsVM?> GetAsync(Guid tenantId, Guid loanId, CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);

            const string headSql = """
select
  lr.id,
  lr.protocol_no as ProtocolNo,
  lr.status::text as Status,
  lr.requester_name as RequesterName,
  lr.requested_at as RequestedAt,
  lr.due_at as DueAt,
  lr.approved_at as ApprovedAt,
  lr.delivered_at as DeliveredAt,
  lr.returned_at as ReturnedAt,
  (select count(*)::int from ged.loan_request_item i
     where i.tenant_id = lr.tenant_id and i.loan_request_id = lr.id and i.reg_status='A') as ItemsCount
from ged.loan_request lr
where lr.tenant_id = @tenant_id and lr.id = @loan_id and lr.reg_status='A';
""";

            var header = await conn.QuerySingleOrDefaultAsync<LoanRowDto>(
                new CommandDefinition(headSql, new { tenant_id = tenantId, loan_id = loanId }, cancellationToken: ct));

            if (header is null) return null;

            const string itemsSql = """
select
  i.document_id as DocumentId,
  i.is_physical as IsPhysical,
  d.code as DocumentCode,
  coalesce(i.description, d.title, i.reference_code, 'Documento solicitado') as DocumentTitle,
  coalesce(dt.name, i.document_type) as DocumentType,
  coalesce(i.is_manual, i.document_id is null) as IsManual,
  i.reference_code as ReferenceCode,
  coalesce(i.description, d.title, i.reference_code, 'Documento solicitado') as Description,
  i.patient_name as PatientName,
  i.medical_record_number as MedicalRecordNumber,
  i.box_code as BoxCode,
  i.physical_location as PhysicalLocation,
  i.notes as Notes
from ged.loan_request_item i
left join ged.document d on d.tenant_id=i.tenant_id and d.id=i.document_id
left join ged.document_type dt on dt.tenant_id=d.tenant_id and dt.id=d.type_id
where i.tenant_id=@tenant_id and i.loan_request_id=@loan_id and i.reg_status='A'
order by coalesce(i.description, d.title, i.reference_code, 'Documento solicitado');
""";

            var items = (await conn.QueryAsync<LoanItemDto>(
                new CommandDefinition(itemsSql, new { tenant_id = tenantId, loan_id = loanId }, cancellationToken: ct)
            )).AsList();

            var historySchemaMissing = string.IsNullOrWhiteSpace(await conn.ExecuteScalarAsync<string?>(
                new CommandDefinition("select to_regclass('ged.loan_request_history')::text", cancellationToken: ct)));

            List<LoanEventDto> history = new();
            if (historySchemaMissing)
            {
                _logger.LogWarning("Histórico de empréstimos ainda não configurado. Tenant={Tenant} Loan={Loan}", tenantId, loanId);
            }
            else
            {
                const string histSql = """
select
  (h.created_at)::timestamp as "EventTime",
  h.action as "EventType",
  coalesce(h.user_name, 'Sistema') as "ByUserName",
  h.reason as "Notes",
  h.old_status as "OldStatus",
  h.new_status as "NewStatus",
  h.reason as "Reason",
  h.internal_notes as "InternalNotes",
  h.sector_name as "Sector",
  h.correlation_id as "CorrelationId"
from ged.loan_request_history h
where h.tenant_id = @tenant_id
  and h.loan_request_id = @loan_id
  and coalesce(h.reg_status, 'A') = 'A'
order by h.created_at desc;
""";

                history = (await conn.QueryAsync<LoanEventDto>(
                    new CommandDefinition(histSql, new { tenant_id = tenantId, loan_id = loanId }, cancellationToken: ct)
                )).AsList();
            }

            return new LoanDetailsVM
            {
                Header = header,
                Items = items,
                History = history,
                HistorySchemaMissing = historySchemaMissing
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LoanQueries.GetAsync failed. Tenant={Tenant} Loan={Loan}", tenantId, loanId);
            return null;
        }
    }

    public async Task<IReadOnlyList<DocumentPickDto>> SearchDocumentsAsync(Guid tenantId, string q, CancellationToken ct)
    {
        try
        {
            await using var con = await _db.OpenAsync(ct);
            q = (q ?? "").Trim();

            if (q.Length == 0)
                return Array.Empty<DocumentPickDto>();

            const string sql = """
select distinct
  d.id   as "Id",
  d.code as "Code",
  d.title as "Title",
  d.status::text as "Status",
  d.created_at as "CreatedAt"
from ged.document d
left join ged.document_version v on v.tenant_id=d.tenant_id and v.document_id=d.id
left join ged.document_search ds on ds.tenant_id=d.tenant_id and ds.document_id=d.id
left join ged.folder f on f.tenant_id=d.tenant_id and f.id=d.folder_id
where d.tenant_id = @TenantId
  and coalesce(d.reg_status,'A') = 'A'
  and d.status <> 'DELETED'::ged.document_status_enum
  and (
        d.code  ilike ('%'||@Q||'%')
     or d.title ilike ('%'||@Q||'%')
     or coalesce(v.file_name,'') ilike ('%'||@Q||'%')
     or coalesce(f.name,'') ilike ('%'||@Q||'%')
     or coalesce(ds.ocr_text,'') ilike ('%'||@Q||'%')
     or coalesce(ds.file_name,'') ilike ('%'||@Q||'%')
  )
order by d.title
limit 20;
""";

            var rows = await con.QueryAsync<DocumentPickDto>(
                new CommandDefinition(sql, new { TenantId = tenantId, Q = q }, cancellationToken: ct));

            return rows.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LoanQueries.SearchDocumentsAsync failed. Tenant={Tenant} Q={Q}", tenantId, q);
            return Array.Empty<DocumentPickDto>();
        }
    }

    public async Task<LoanStatsDto> StatsAsync(Guid tenantId, CancellationToken ct)
    {
        try
        {
            await using var con = await _db.OpenAsync(ct);

            const string sql = """
select
  count(*)::int as Total,
  count(*) filter (where returned_at is null)::int as Open,
  count(*) filter (where returned_at is null and due_at < now())::int as Overdue,
  count(*) filter (where status::text='REQUESTED')::int as Requested,
  count(*) filter (where status::text='APPROVED')::int as Approved,
  count(*) filter (where status::text='DELIVERED')::int as Delivered,
  count(*) filter (where status::text='RETURNED')::int as Returned
from ged.loan_request
where tenant_id=@TenantId and reg_status='A';
""";

            return await con.QuerySingleAsync<LoanStatsDto>(
                new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LoanQueries.StatsAsync failed. Tenant={Tenant}", tenantId);
            // fallback pra não quebrar a Index
            return new LoanStatsDto();
        }
    }
     
}