using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Loans;
using InovaGed.Domain.Primitives;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged.Loans;

public sealed class LoanCommands : ILoanCommands
{
    private readonly IDbConnectionFactory _db;
    private readonly IAuditWriter _audit;
    private readonly ILogger<LoanCommands> _logger;

    public LoanCommands(IDbConnectionFactory db, IAuditWriter audit, ILogger<LoanCommands> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Result<Guid>> CreateAsync(Guid tenantId, Guid? userId, LoanCreateVM vm, CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty)
                return Result<Guid>.Fail("TENANT", "Tenant inválido.");

            if (userId is null || userId == Guid.Empty)
                return Result<Guid>.Fail("USER", "Usuário logado não identificado para requester_id.");

            var docIds = (vm.DocumentIds ?? new List<Guid>())
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToList();

            if (docIds.Count == 0)
                return Result<Guid>.Fail("DOC", "Informe pelo menos 1 documento para o empréstimo.");

            await using var con = await _db.OpenAsync(ct);
            await using var tx = await con.BeginTransactionAsync(ct);

            var nowUtc = DateTimeOffset.UtcNow;

            DateTimeOffset dueUtc;
            if (vm.DueAt.HasValue)
            {
                var due = vm.DueAt.Value;

                if (due.TimeOfDay == TimeSpan.Zero)
                    due = new DateTimeOffset(due.Year, due.Month, due.Day, 23, 59, 59, due.Offset);

                dueUtc = due.ToUniversalTime();
            }
            else
            {
                dueUtc = nowUtc.AddDays(7);
            }

            const string sqlHead = """
insert into ged.loan_request
(
  id,
  tenant_id,
  protocol_no,
  status,
  requester_id,
  requester_name,
  document_id,
  requested_at,
  due_at,
  reg_status
)
values
(
  gen_random_uuid(),
  @TenantId,
  (select coalesce(max(protocol_no),0)+1 from ged.loan_request where tenant_id=@TenantId),
  'REQUESTED'::ged.loan_status,
  @RequesterId,
  @RequesterName,
  @DocumentId,
  @RequestedAt,
  @DueAt,
  'A'
)
returning id;
""";

            var loanId = await con.ExecuteScalarAsync<Guid>(
                new CommandDefinition(
                    sqlHead,
                    new
                    {
                        TenantId = tenantId,
                        RequesterId = userId.Value,
                        RequesterName = (vm.RequesterName ?? "").Trim(),
                        DocumentId = docIds[0],          // ✅ obrigatório no seu schema
                        RequestedAt = nowUtc,
                        DueAt = dueUtc
                    },
                    transaction: tx,
                    cancellationToken: ct));

            // Se você tiver a tabela de itens, mantém (multi-documentos):
            const string sqlItem = """
insert into ged.loan_request_item
(
  tenant_id,
  loan_id,
  document_id,
  is_physical,
  reg_status
)
values
(
  @TenantId,
  @LoanId,
  @DocumentId,
  @IsPhysical,
  'A'
);
""";

            foreach (var docId in docIds)
            {
                await con.ExecuteAsync(
                    new CommandDefinition(
                        sqlItem,
                        new
                        {
                            TenantId = tenantId,
                            LoanId = loanId,
                            DocumentId = docId,
                            IsPhysical = vm.IsPhysical
                        },
                        transaction: tx,
                        cancellationToken: ct));
            }

            const string sqlHist = """
insert into ged.loan_history
(
  tenant_id,
  loan_id,
  event_time,
  event_type,
  by_user_id,
  notes,
  reg_status
)
values
(
  @TenantId,
  @LoanId,
  @EventTime,
  'REQUESTED',
  @ByUserId,
  @Notes,
  'A'
);
""";

            await con.ExecuteAsync(
                new CommandDefinition(
                    sqlHist,
                    new
                    {
                        TenantId = tenantId,
                        LoanId = loanId,
                        EventTime = nowUtc,
                        ByUserId = userId.Value,
                        Notes = (vm.Notes ?? "").Trim()
                    },
                    transaction: tx,
                    cancellationToken: ct));

            await tx.CommitAsync(ct);
            return Result<Guid>.Ok(loanId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LoanCommands.CreateAsync failed. Tenant={Tenant}", tenantId);
            return Result<Guid>.Fail("LOAN", "Erro ao criar empréstimo.");
        }
    }
    public Task<Result> ApproveAsync(Guid tenantId, Guid loanId, Guid? userId, string? notes, CancellationToken ct)
        => TransitionAsync(tenantId, loanId, userId, notes, newStatus: "APPROVED", label: "Aprovar", setReturnedAt: false, ct);

    public Task<Result> DeliverAsync(Guid tenantId, Guid loanId, Guid? userId, string? notes, CancellationToken ct)
        => TransitionAsync(tenantId, loanId, userId, notes, newStatus: "DELIVERED", label: "Entregar", setReturnedAt: false, ct);

    public Task<Result> ReturnAsync(Guid tenantId, Guid loanId, Guid? userId, string? notes, CancellationToken ct)
        => TransitionAsync(tenantId, loanId, userId, notes, newStatus: "RETURNED", label: "Devolver", setReturnedAt: true, ct);

    // Compat
    public Task<Result> MarkDeliveredAsync(Guid tenantId, Guid loanId, Guid userId, string? notes, CancellationToken ct)
        => DeliverAsync(tenantId, loanId, userId, notes, ct);

    public Task<Result> MarkReturnedAsync(Guid tenantId, Guid loanId, Guid userId, string? notes, CancellationToken ct)
        => ReturnAsync(tenantId, loanId, userId, notes, ct);

    public async Task<int> RunOverdueAsync(Guid tenantId, CancellationToken ct)
    {
        if (tenantId == Guid.Empty) return 0;

        try
        {
            await using var conn = await _db.OpenAsync(ct);

            const string tryFn = "select ged.loan_run_overdue(@tenant_id);";
            var updated = await conn.ExecuteScalarAsync<int>(
                new CommandDefinition(tryFn, new { tenant_id = tenantId }, cancellationToken: ct));

            _ = await _audit.WriteAsync(
                tenantId, null,
                action: "LOAN_OVERDUE_RUN",
                entityName: "loan_request",
                entityId: null,
                summary: "Rotina OVERDUE executada via função ged.loan_run_overdue",
                ipAddress: null,
                userAgent: null,
                data: new { updated },
                ct: ct);

            return updated;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RunOverdueAsync: função ged.loan_run_overdue não disponível. Usando fallback vw_loan_overdue. Tenant={Tenant}", tenantId);

            var res = await RegisterOverdueEventsAsync(tenantId, userId: null, ct);
            return res.IsSuccess ? res.Value : 0;
        }
    }

    public async Task<Result<int>> RegisterOverdueEventsAsync(Guid tenantId, Guid? userId, CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty)
                return Result<int>.Fail("TENANT", "Tenant inválido.");

            await using var conn = await _db.OpenAsync(ct);

            var nowUtc = DateTimeOffset.UtcNow;

            const string sql = """
insert into ged.loan_history(tenant_id, loan_id, event_time, event_type, by_user_id, notes, reg_date, reg_status)
select
  lr.tenant_id,
  lr.id,
  @nowUtc,
  'OVERDUE',
  @user_id,
  'Empréstimo vencido (registro automático).',
  @nowUtc,
  'A'
from ged.vw_loan_overdue lr
where lr.tenant_id=@tenant_id
  and not exists (
    select 1 from ged.loan_history h
    where h.tenant_id=lr.tenant_id and h.loan_id=lr.id and h.event_type='OVERDUE' and h.reg_status='A'
  );
""";

            var inserted = await conn.ExecuteAsync(
                new CommandDefinition(sql, new { tenant_id = tenantId, user_id = userId, nowUtc }, cancellationToken: ct));

            _ = await _audit.WriteAsync(
                tenantId, userId,
                action: "LOAN_OVERDUE_REGISTER",
                entityName: "loan_request",
                entityId: null,
                summary: "Registro automático de vencidos (OVERDUE)",
                ipAddress: null,
                userAgent: null,
                data: new { inserted },
                ct: ct);

            return Result<int>.Ok(inserted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LoanCommands.RegisterOverdueEventsAsync failed. Tenant={Tenant}", tenantId);
            return Result<int>.Fail("LOAN_OVERDUE", "Falha ao registrar eventos de vencimento.");
        }
    }

    private async Task<Result> TransitionAsync(
        Guid tenantId,
        Guid loanId,
        Guid? userId,
        string? notes,
        string newStatus,
        string label,
        bool setReturnedAt,
        CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty) return Result.Fail("TENANT", "Tenant inválido.");
            if (loanId == Guid.Empty) return Result.Fail("ID", "LoanId inválido.");
            if (string.IsNullOrWhiteSpace(newStatus)) return Result.Fail("STATUS", "Status inválido.");

            await using var conn = await _db.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            var nowUtc = DateTimeOffset.UtcNow;

            const string upd = """
update ged.loan_request
set status = (@status)::ged.loan_status_enum,
    approved_at  = case when @status='APPROVED'  then @nowUtc else approved_at end,
    delivered_at = case when @status='DELIVERED' then @nowUtc else delivered_at end,
    returned_at  = case when @set_returned then @nowUtc else returned_at end
where tenant_id=@tenant_id
  and id=@loan_id
  and reg_status='A';
""";

            var rows = await conn.ExecuteAsync(
                new CommandDefinition(upd, new
                {
                    tenant_id = tenantId,
                    loan_id = loanId,
                    status = newStatus,
                    set_returned = setReturnedAt,
                    nowUtc
                }, transaction: tx, cancellationToken: ct));

            if (rows == 0)
            {
                await tx.RollbackAsync(ct);
                return Result.Fail("NOTFOUND", "Empréstimo não encontrado.");
            }

            const string hist = """
insert into ged.loan_history(tenant_id, loan_id, event_time, event_type, by_user_id, notes, reg_date, reg_status)
values(@tenant_id, @loan_id, @nowUtc, @event_type, @by_user_id, @notes, @nowUtc, 'A');
""";

            await conn.ExecuteAsync(
                new CommandDefinition(hist, new
                {
                    tenant_id = tenantId,
                    loan_id = loanId,
                    event_type = newStatus,
                    by_user_id = userId,
                    notes = (notes ?? "").Trim(),
                    nowUtc
                }, transaction: tx, cancellationToken: ct));

            await tx.CommitAsync(ct);

            _ = await _audit.WriteAsync(
                tenantId, userId,
                action: "LOAN_EVENT",
                entityName: "loan_request",
                entityId: loanId,
                summary: $"{label} empréstimo",
                ipAddress: null,
                userAgent: null,
                data: new { status = newStatus, notes },
                ct: ct);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LoanCommands.TransitionAsync failed. Tenant={Tenant} Loan={Loan}", tenantId, loanId);
            return Result.Fail("LOAN_TRANSITION", "Falha ao atualizar status do empréstimo.");
        }
    }
}