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
            if (tenantId == Guid.Empty) return Result<Guid>.Fail("TENANT", "Tenant inválido.");
            if (vm is null) return Result<Guid>.Fail("VM", "Dados inválidos.");
            if (string.IsNullOrWhiteSpace(vm.RequesterName)) return Result<Guid>.Fail("REQ", "Solicitante é obrigatório.");
            if (vm.DocumentIds is null || vm.DocumentIds.Count == 0) return Result<Guid>.Fail("DOCS", "Selecione ao menos 1 documento.");

            await using var conn = await _db.OpenAsync(ct);
            using var tx = conn.BeginTransaction();

            // protocolo simples (pode trocar por sequence do banco depois)
            var protocol = $"EMP-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}".Substring(0, 30);

            const string insertLoan = @"
insert into ged.loan_request
(id, tenant_id, protocol_no, requester_id, requester_name, due_at, is_physical, status, requested_at, created_by, notes, reg_date, reg_status)
values
(gen_random_uuid(), @tenant_id, @protocol_no, @requester_id, @requester_name, @due_at, @is_physical,
 'REQUESTED'::ged.loan_status, now(), @created_by, @notes, now(), 'A')
returning id;
";
            var loanId = await conn.ExecuteScalarAsync<Guid>(
                new CommandDefinition(insertLoan, new
                {
                    tenant_id = tenantId,
                    protocol_no = protocol,
                    requester_id = vm.RequesterId,
                    requester_name = vm.RequesterName,
                    due_at = vm.DueAt,
                    is_physical = vm.IsPhysical,
                    created_by = userId,
                    notes = vm.Notes
                }, transaction: tx, cancellationToken: ct));

            const string insertItem = @"
insert into ged.loan_request_item(tenant_id, loan_id, document_id, is_physical, reg_date, reg_status)
values (@tenant_id, @loan_id, @document_id, @is_physical, now(), 'A')
on conflict (tenant_id, loan_id, document_id)
do update set reg_status='A';
";
            foreach (var docId in vm.DocumentIds.Distinct())
            {
                await conn.ExecuteAsync(
                    new CommandDefinition(insertItem, new
                    {
                        tenant_id = tenantId,
                        loan_id = loanId,
                        document_id = docId,
                        is_physical = vm.IsPhysical
                    }, transaction: tx, cancellationToken: ct));
            }

            const string hist = @"
insert into ged.loan_history(tenant_id, loan_id, event_time, event_type, by_user_id, notes, reg_date, reg_status)
values(@tenant_id, @loan_id, now(), 'REQUESTED', @by_user_id, @notes, now(), 'A');
";
            await conn.ExecuteAsync(
                new CommandDefinition(hist, new
                {
                    tenant_id = tenantId,
                    loan_id = loanId,
                    by_user_id = userId,
                    notes = vm.Notes
                }, transaction: tx, cancellationToken: ct));

            tx.Commit();

            // auditoria não pode derrubar o fluxo
            _ = await _audit.WriteAsync(
                tenantId, userId,
                action: "LOAN_EVENT",
                entityName: "loan_request",
                entityId: loanId,
                summary: "Solicitação de empréstimo criada",
                ipAddress: null,
                userAgent: null,
                data: new { protocol, docs = vm.DocumentIds.Count, dueAt = vm.DueAt, isPhysical = vm.IsPhysical },
                ct: ct);

            return Result<Guid>.Ok(loanId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LoanCommands.CreateAsync failed. Tenant={Tenant}", tenantId);
            return Result<Guid>.Fail("LOAN", "Falha ao criar empréstimo.");
        }
    }

    public Task<Result> ApproveAsync(Guid tenantId, Guid loanId, Guid? userId, string? notes, CancellationToken ct)
        => TransitionAsync(tenantId, loanId, userId, notes, newStatus: "APPROVED", label: "Aprovar", setReturnedAt: false, ct);

    public Task<Result> DeliverAsync(Guid tenantId, Guid loanId, Guid? userId, string? notes, CancellationToken ct)
        => TransitionAsync(tenantId, loanId, userId, notes, newStatus: "DELIVERED", label: "Entregar", setReturnedAt: false, ct);

    public Task<Result> ReturnAsync(Guid tenantId, Guid loanId, Guid? userId, string? notes, CancellationToken ct)
        => TransitionAsync(tenantId, loanId, userId, notes, newStatus: "RETURNED", label: "Devolver", setReturnedAt: true, ct);

    // ==========================
    // MÉTODOS "DUPLICADOS" DO ILoanCommands (compatibilidade)
    // ==========================

    public Task<Result> MarkDeliveredAsync(Guid tenantId, Guid loanId, Guid userId, string? notes, CancellationToken ct)
        => DeliverAsync(tenantId, loanId, userId, notes, ct);

    public Task<Result> MarkReturnedAsync(Guid tenantId, Guid loanId, Guid userId, string? notes, CancellationToken ct)
        => ReturnAsync(tenantId, loanId, userId, notes, ct);

    /// <summary>
    /// Executa rotina de vencidos. Tenta a função ged.loan_run_overdue(tenant) se existir.
    /// Caso não exista, usa a lógica atual via vw_loan_overdue (RegisterOverdueEventsAsync).
    /// </summary>
    public async Task<int> RunOverdueAsync(Guid tenantId, CancellationToken ct)
    {
        if (tenantId == Guid.Empty) return 0;

        try
        {
            await using var conn = await _db.OpenAsync(ct);

            // tenta função (se existir no seu banco)
            const string tryFn = @"select ged.loan_run_overdue(@tenant_id);";
            var updated = await conn.ExecuteScalarAsync<int>(
                new CommandDefinition(tryFn, new { tenant_id = tenantId }, cancellationToken: ct));

            _ = await _audit.WriteAsync(
                tenantId, null,
                action: "LOAN_EVENT",
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
            // fallback para o método por view (não quebra a execução)
            _logger.LogWarning(ex, "RunOverdueAsync: função ged.loan_run_overdue não disponível. Usando fallback vw_loan_overdue. Tenant={Tenant}", tenantId);

            var res = await RegisterOverdueEventsAsync(tenantId, userId: null, ct);
            return res.IsSuccess ? res.Value : 0;
        }
    }

    // ==========================
    // OVERDUE via VIEW (já existia)
    // ==========================

    public async Task<Result<int>> RegisterOverdueEventsAsync(Guid tenantId, Guid? userId, CancellationToken ct)
    {
        try
        {
            if (tenantId == Guid.Empty) return Result<int>.Fail("TENANT", "Tenant inválido.");

            await using var conn = await _db.OpenAsync(ct);

            const string sql = @"
insert into ged.loan_history(tenant_id, loan_id, event_time, event_type, by_user_id, notes, reg_date, reg_status)
select
  lr.tenant_id,
  lr.id,
  now(),
  'OVERDUE',
  @user_id,
  'Empréstimo vencido (registro automático).',
  now(),
  'A'
from ged.vw_loan_overdue lr
where lr.tenant_id=@tenant_id
  and not exists (
    select 1 from ged.loan_history h
    where h.tenant_id=lr.tenant_id and h.loan_id=lr.id and h.event_type='OVERDUE' and h.reg_status='A'
  );
";
            var count = await conn.ExecuteAsync(
                new CommandDefinition(sql, new { tenant_id = tenantId, user_id = userId }, cancellationToken: ct));

            _ = await _audit.WriteAsync(
                tenantId, userId,
                action: "LOAN_EVENT",
                entityName: "loan_request",
                entityId: null,
                summary: "Registro automático de vencidos (OVERDUE)",
                ipAddress: null,
                userAgent: null,
                data: new { created = count },
                ct: ct);

            return Result<int>.Ok(count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LoanCommands.RegisterOverdueEventsAsync failed. Tenant={Tenant}", tenantId);
            return Result<int>.Fail("LOAN", "Falha ao registrar eventos de vencimento.");
        }
    }

    // ==========================
    // TRANSIÇÃO (corrigida p/ usar ct e não CancellationToken.None)
    // ==========================

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
            using var tx = conn.BeginTransaction();

            // (Opcional) garantir transição válida:
            // - APPROVED só se REQUESTED
            // - DELIVERED só se APPROVED
            // - RETURNED só se DELIVERED ou OVERDUE
            // Se quiser, ativamos isso depois sem quebrar PoC.

            const string upd = @"
update ged.loan_request
set status = @status::ged.loan_status,
    approved_at  = case when @status='APPROVED'  then now() else approved_at end,
    delivered_at = case when @status='DELIVERED' then now() else delivered_at end,
    returned_at  = case when @set_returned then now() else returned_at end
where tenant_id=@tenant_id
  and id=@loan_id
  and reg_status='A';
";
            var rows = await conn.ExecuteAsync(
                new CommandDefinition(upd, new
                {
                    tenant_id = tenantId,
                    loan_id = loanId,
                    status = newStatus,
                    set_returned = setReturnedAt
                }, transaction: tx, cancellationToken: ct));

            if (rows == 0)
            {
                tx.Rollback();
                return Result.Fail("NOTFOUND", "Empréstimo não encontrado.");
            }

            const string hist = @"
insert into ged.loan_history(tenant_id, loan_id, event_time, event_type, by_user_id, notes, reg_date, reg_status)
values(@tenant_id, @loan_id, now(), @event_type, @by_user_id, @notes, now(), 'A');
";
            await conn.ExecuteAsync(
                new CommandDefinition(hist, new
                {
                    tenant_id = tenantId,
                    loan_id = loanId,
                    event_type = newStatus,
                    by_user_id = userId,
                    notes
                }, transaction: tx, cancellationToken: ct));

            tx.Commit();

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
            return Result.Fail("LOAN", "Falha ao atualizar status do empréstimo.");
        }
    }
}