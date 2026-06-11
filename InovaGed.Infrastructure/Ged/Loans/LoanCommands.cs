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
            var manualItems = (vm.ManualItems ?? new List<LoanManualItemVM>())
                .Where(x => !string.IsNullOrWhiteSpace(x.Description) || !string.IsNullOrWhiteSpace(x.ReferenceCode))
                .ToList();

            if (docIds.Count == 0 && manualItems.Count == 0)
                return Result<Guid>.Fail("DOC", "Adicione pelo menos um documento do GED ou informe um documento manualmente para solicitar.");

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
  requester_sector,
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
  (select nullif(coalesce(s.setor, s.lotacao, ''), '') from ged.app_user u left join ged.servidor s on s.tenant_id=u.tenant_id and s.id=u.servidor_id where u.tenant_id=@TenantId and u.id=@RequesterId limit 1),
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
                        DocumentId = docIds.Count == 0 ? (Guid?)null : docIds[0],
                        RequestedAt = nowUtc,
                        DueAt = dueUtc
                    },
                    transaction: tx,
                    cancellationToken: ct));

            // Se você tiver a tabela de itens, mantém (multi-documentos):
            const string sqlItem = """
insert into ged.loan_request_item
(
  id,
  tenant_id,
  loan_request_id,
  document_id,
  is_manual,
  is_physical,
  reference_code,
  description,
  document_type,
  patient_name,
  medical_record_number,
  box_code,
  physical_location,
  notes,
  created_at,
  reg_status
)
values
(
  gen_random_uuid(),
  @TenantId,
  @LoanId,
  @DocumentId,
  @IsManual,
  @IsPhysical,
  @ReferenceCode,
  @Description,
  @DocumentType,
  @PatientName,
  @MedicalRecordNumber,
  @BoxCode,
  @PhysicalLocation,
  @ItemNotes,
  now(),
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
                            DocumentId = (Guid?)docId,
                            IsManual = false,
                            IsPhysical = vm.IsPhysical,
                            ReferenceCode = (string?)null,
                            Description = (string?)null,
                            DocumentType = (string?)null,
                            PatientName = (string?)null,
                            MedicalRecordNumber = (string?)null,
                            BoxCode = (string?)null,
                            PhysicalLocation = (string?)null,
                            ItemNotes = (string?)null
                        },
                        transaction: tx,
                        cancellationToken: ct));
            }

            foreach (var manual in manualItems)
            {
                await con.ExecuteAsync(
                    new CommandDefinition(
                        sqlItem,
                        new
                        {
                            TenantId = tenantId,
                            LoanId = loanId,
                            DocumentId = (Guid?)null,
                            IsManual = true,
                            IsPhysical = vm.IsPhysical,
                            ReferenceCode = TrimOrNull(manual.ReferenceCode),
                            Description = TrimOrNull(manual.Description),
                            DocumentType = TrimOrNull(manual.DocumentType),
                            PatientName = TrimOrNull(manual.PatientName),
                            MedicalRecordNumber = TrimOrNull(manual.MedicalRecordNumber),
                            BoxCode = TrimOrNull(manual.BoxCode),
                            PhysicalLocation = TrimOrNull(manual.PhysicalLocation),
                            ItemNotes = TrimOrNull(manual.Notes)
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
                        Notes = $"[STATUS=REQUESTED] {(vm.Notes ?? string.Empty).Trim()}".Trim()
                    },
                    transaction: tx,
                    cancellationToken: ct));

            await WriteRichHistoryAsync(con, tx, tenantId, loanId, oldStatus: null, newStatus: "REQUESTED", action: "LOAN_CREATED", userId, reason: vm.Notes, internalNotes: null, ct);

            await tx.CommitAsync(ct);

            _ = await _audit.WriteAsync(
                tenantId, userId,
                action: "LOAN_CREATED",
                entityName: "loan_request",
                entityId: loanId,
                summary: "Solicitação de empréstimo criada",
                ipAddress: null,
                userAgent: null,
                data: new { loanId, previousStatus = (string?)null, newStatus = "REQUESTED", correlationId = Guid.NewGuid().ToString("N"), timestampUtc = nowUtc },
                ct: ct);

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

    public Task<Result> RejectAsync(Guid tenantId, Guid loanId, Guid? userId, string? notes, CancellationToken ct)
        => TransitionAsync(tenantId, loanId, userId, notes, newStatus: "REJECTED", label: "Rejeitar", setReturnedAt: false, ct);

    public Task<Result> ReturnForAdjustmentAsync(Guid tenantId, Guid loanId, Guid? userId, string? notes, CancellationToken ct)
        => TransitionAsync(tenantId, loanId, userId, notes, newStatus: "RETURNED_FOR_ADJUSTMENT", label: "Devolver para ajuste", setReturnedAt: false, ct);

    public Task<Result> CancelAsync(Guid tenantId, Guid loanId, Guid? userId, string? notes, CancellationToken ct)
        => TransitionAsync(tenantId, loanId, userId, notes, newStatus: "CANCELLED", label: "Cancelar", setReturnedAt: false, ct);

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

            var historyExists = await conn.ExecuteScalarAsync<string?>(
                new CommandDefinition("select to_regclass('ged.loan_request_history')::text", cancellationToken: ct));

            if (string.IsNullOrWhiteSpace(historyExists))
            {
                _logger.LogWarning(
                    "Histórico de Loans não configurado. Ignorando registro de overdue events. Tenant={TenantId}",
                    tenantId);
                return Result<int>.Ok(0);
            }

            // Regra de negócio: o worker registra o vencimento no histórico rico e atualiza o status textual para OVERDUE
            // apenas para empréstimos em estados operacionais abertos (APPROVED/DELIVERED ou equivalentes PT-BR).
            // A guarda por h.created_at::date evita duplicidade a cada execução diária do worker.
            const string richHistorySql = """
insert into ged.loan_request_history (
    tenant_id,
    loan_request_id,
    old_status,
    new_status,
    action,
    user_id,
    user_name,
    reason,
    internal_notes,
    metadata_json,
    created_at,
    reg_status
)
select
    l.tenant_id,
    l.id,
    l.status::text,
    'OVERDUE',
    'LOAN_OVERDUE',
    @UserId,
    'Sistema - Verificação de vencimento',
    'Empréstimo vencido',
    'Registro automático de vencimento pelo LoanOverdueWorker',
    jsonb_build_object(
        'dueAt', l.due_at,
        'protocolNo', l.protocol_no
    ),
    now(),
    'A'
from ged.loan_request l
where l.tenant_id = @TenantId
  and coalesce(l.reg_status, 'A') = 'A'
  and l.due_at is not null
  and l.due_at < now()
  and upper(l.status::text) not in ('RETURNED','DEVOLVIDO','CANCELLED','CANCELADO','CANCELED','OVERDUE','VENCIDO')
  and not exists (
      select 1
      from ged.loan_request_history h
      where h.tenant_id = l.tenant_id
        and h.loan_request_id = l.id
        and h.action = 'LOAN_OVERDUE'
        and h.created_at::date = now()::date
        and h.old_status is not distinct from l.status::text
        and coalesce(h.reg_status, 'A') = 'A'
  );
""";

            var inserted = await conn.ExecuteAsync(
                new CommandDefinition(richHistorySql, new { TenantId = tenantId, UserId = userId }, cancellationToken: ct));

            const string legacyHistorySql = """
insert into ged.loan_history(tenant_id, loan_id, event_time, event_type, by_user_id, notes, reg_date, reg_status)
select
  l.tenant_id,
  l.id,
  now(),
  'OVERDUE',
  @UserId,
  'Empréstimo vencido (registro automático).',
  now(),
  'A'
from ged.loan_request l
where l.tenant_id = @TenantId
  and coalesce(l.reg_status, 'A') = 'A'
  and l.due_at is not null
  and l.due_at < now()
  and upper(l.status::text) not in ('RETURNED','DEVOLVIDO','CANCELLED','CANCELADO','CANCELED','OVERDUE','VENCIDO')
  and not exists (
    select 1
    from ged.loan_history h
    where h.tenant_id = l.tenant_id
      and h.loan_id = l.id
      and h.event_type = 'OVERDUE'
      and h.event_time::date = now()::date
      and coalesce(h.reg_status, 'A') = 'A'
  );
""";

            await conn.ExecuteAsync(
                new CommandDefinition(legacyHistorySql, new { TenantId = tenantId, UserId = userId }, cancellationToken: ct));

            var canUpdateOverdueStatus = await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
select to_regtype('ged.loan_status') is not null
   and exists (select 1 from pg_enum e join pg_type t on t.oid = e.enumtypid join pg_namespace n on n.oid = t.typnamespace where n.nspname = 'ged' and t.typname = 'loan_status' and e.enumlabel = 'OVERDUE')
   and exists (select 1 from information_schema.columns where table_schema = 'ged' and table_name = 'loan_request' and column_name = 'updated_at')
   and exists (select 1 from information_schema.columns where table_schema = 'ged' and table_name = 'loan_request' and column_name = 'updated_by');
""", cancellationToken: ct));

            if (canUpdateOverdueStatus)
            {
                const string updateSql = """
update ged.loan_request
set
    status = 'OVERDUE'::ged.loan_status,
    updated_at = now(),
    updated_by = @UserId
where tenant_id = @TenantId
  and coalesce(reg_status, 'A') = 'A'
  and due_at is not null
  and due_at < now()
  and upper(status::text) in ('DELIVERED','ENTREGUE','APPROVED','APROVADO');
""";

                await conn.ExecuteAsync(
                    new CommandDefinition(updateSql, new { TenantId = tenantId, UserId = userId }, cancellationToken: ct));
            }

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

            if (newStatus == "APPROVED")
            {
                const string sqlAuth = """
select exists (
  select 1
  from ged.loan_approval_profile p
  join aspnetuserroles ur on ur.roleid = p.role_id
  where p.tenant_id=@TenantId and ur.userid=@UserId and p.reg_status='A'
);
""";
                var canApprove = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(sqlAuth, new { TenantId = tenantId, UserId = userId }, transaction: tx, cancellationToken: ct));
                // A autorização principal do fluxo operacional é feita no controller/policy.
                // Perfis cadastrados continuam funcionando como trilha configurável sem bloquear ADMIN/ADMINISTRADOROPHIR.
                if (!canApprove)
                    _logger.LogInformation("Aprovação de empréstimo sem perfil configurado em ged.loan_approval_profile. Tenant={TenantId} User={UserId}", tenantId, userId);
            }

            var nowUtc = DateTimeOffset.UtcNow;
            var previousStatus = await conn.ExecuteScalarAsync<string?>(new CommandDefinition("""
select status::text from ged.loan_request
where tenant_id=@tenant_id and id=@loan_id and reg_status='A'
limit 1;
""", new { tenant_id = tenantId, loan_id = loanId }, transaction: tx, cancellationToken: ct));

            const string upd = """
update ged.loan_request
set status = (@status)::ged.loan_status,
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
                    notes = $"[STATUS={newStatus}] {(notes ?? "").Trim()}".Trim(),
                    nowUtc
                }, transaction: tx, cancellationToken: ct));

            await WriteRichHistoryAsync(conn, tx, tenantId, loanId, previousStatus, newStatus, ActionForStatus(newStatus), userId, notes, internalNotes: null, ct);

            await tx.CommitAsync(ct);

            _ = await _audit.WriteAsync(
                tenantId, userId,
                action: newStatus switch
                {
                    "APPROVED" => "LOAN_APPROVED",
                    "DELIVERED" => "LOAN_DELIVERED",
                    "RETURNED" => "LOAN_RETURNED",
                    "RETURNED_FOR_ADJUSTMENT" => "LOAN_RETURNED_FOR_ADJUSTMENT",
                    "CANCELLED" or "CANCELED" => "LOAN_CANCELLED",
                    "REJECTED" => "LOAN_REJECTED",
                    _ => "LOAN_EVENT"
                },
                entityName: "loan_request",
                entityId: loanId,
                summary: $"{label} empréstimo",
                ipAddress: null,
                userAgent: null,
                data: new { loanId, previousStatus, newStatus, notes, correlationId = Guid.NewGuid().ToString("N"), timestampUtc = nowUtc },
                ct: ct);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LoanCommands.TransitionAsync failed. Tenant={Tenant} Loan={Loan}", tenantId, loanId);
            return Result.Fail("LOAN_TRANSITION", "Falha ao atualizar status do empréstimo.");
        }
    }

    private static string? TrimOrNull(string? value)
    {
        value = value?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string ActionForStatus(string status) => status switch
    {
        "APPROVED" => "LOAN_APPROVED",
        "REJECTED" => "LOAN_REJECTED",
        "DELIVERED" => "LOAN_DELIVERED",
        "RETURNED" => "LOAN_RETURNED",
        "CANCELLED" or "CANCELED" => "LOAN_CANCELLED",
        "RETURNED_FOR_ADJUSTMENT" => "LOAN_RETURNED_FOR_ADJUSTMENT",
        "OVERDUE" => "LOAN_OVERDUE",
        _ => "LOAN_EVENT"
    };

    private async Task WriteRichHistoryAsync(
        System.Data.Common.DbConnection conn,
        System.Data.Common.DbTransaction tx,
        Guid tenantId,
        Guid loanId,
        string? oldStatus,
        string? newStatus,
        string action,
        Guid? userId,
        string? reason,
        string? internalNotes,
        CancellationToken ct)
    {
        try
        {
            var historyExists = await conn.ExecuteScalarAsync<string?>(
                new CommandDefinition("select to_regclass('ged.loan_request_history')::text", transaction: tx, cancellationToken: ct));

            if (string.IsNullOrWhiteSpace(historyExists))
            {
                _logger.LogWarning(
                    "Loan history table missing. Skipping rich history registration. Tenant={TenantId} Loan={LoanId} Action={Action}",
                    tenantId,
                    loanId,
                    action);
                return;
            }

            const string sql = """
insert into ged.loan_request_history
(tenant_id, loan_request_id, old_status, new_status, action, user_id, user_name, sector_id, sector_name, reason, internal_notes, metadata_json, created_at, correlation_id, reg_status)
select @TenantId, @LoanId, @OldStatus, @NewStatus, @Action, @UserId,
       coalesce(u.name, u.email, @UserId::text, 'Sistema'),
       s.id,
       nullif(coalesce(s.setor, s.lotacao, ''), ''),
       @Reason, @InternalNotes, '{}'::jsonb, now(), @CorrelationId, 'A'
from (select 1) seed
left join ged.app_user u on u.tenant_id=@TenantId and u.id=@UserId
left join ged.servidor s on s.tenant_id=u.tenant_id and s.id=u.servidor_id
;
""";
            await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                TenantId = tenantId,
                LoanId = loanId,
                OldStatus = oldStatus,
                NewStatus = newStatus,
                Action = action,
                UserId = userId,
                Reason = TrimOrNull(reason),
                InternalNotes = TrimOrNull(internalNotes),
                CorrelationId = Guid.NewGuid().ToString("N")
            }, tx, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            // O histórico rico não deve derrubar o fluxo transacional principal; a migration e o SchemaRepair corrigem o schema definitivo.
            _logger.LogError(ex, "Falha ao gravar histórico rico de empréstimo. Fluxo principal preservado. Tenant={TenantId} Loan={LoanId} Action={Action}", tenantId, loanId, action);
        }
    }

}
