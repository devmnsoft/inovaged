using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Loans;
using InovaGed.Domain.Primitives;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged.Loans;

public sealed class LoanOverdueService(ILoanCommands commands) : ILoanOverdueService
{
    public async Task<int> RunAsync(Guid tenantId, Guid? actorId, CancellationToken ct)
    {
        var result = await commands.RegisterOverdueEventsAsync(tenantId, actorId, ct);
        return result.IsSuccess ? result.Value : throw new InvalidOperationException(result.ErrorMessage);
    }
}

public sealed class LoanCollectionService(IDbConnectionFactory db, IAuditWriter audit, ILogger<LoanCollectionService> logger) : ILoanCollectionService
{
    public async Task<Result> CollectAsync(Guid tenantId, Guid loanId, Guid? actorId, string? message, CancellationToken ct)
    {
        if (tenantId == Guid.Empty || loanId == Guid.Empty) return Result.Fail("INVALID", "Empréstimo inválido.");
        try
        {
            await using var conn = await db.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            var loan = await conn.QuerySingleOrDefaultAsync<(string Status, int Count)>(new CommandDefinition("select status::text as Status, coalesce(collection_count,0) as Count from ged.loan_request where tenant_id=@tenantId and id=@loanId and coalesce(reg_status,'A')='A' for update", new { tenantId, loanId }, tx, cancellationToken: ct));
            if (string.IsNullOrEmpty(loan.Status)) return Result.Fail("NOT_FOUND", "Empréstimo não encontrado.");
            if (new[] { "RETURNED", "REJECTED", "CANCELLED", "CANCELED" }.Contains(loan.Status.ToUpperInvariant())) return Result.Fail("CLOSED", "Não é possível cobrar um empréstimo encerrado.");
            var level = loan.Count switch { 0 => "FIRST_NOTICE", 1 => "SECOND_NOTICE", 2 => "ESCALATED", _ => "FINAL_NOTICE" };
            var text = string.IsNullOrWhiteSpace(message) ? $"Solicitamos a devolução do empréstimo. Nível: {level}." : message.Trim();
            await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.loan_collection_event(tenant_id, loan_request_id, level, channel, delivery_status, message, created_by)
values(@tenantId,@loanId,@level,'INTERNAL','PENDING_EXTERNAL',@text,@actorId);
insert into ged.loan_request_message(tenant_id,loan_request_id,sender_user_id,sender_name,sender_role,message,message_type,is_internal)
values(@tenantId,@loanId,@actorId,'Sistema de Cobrança','SYSTEM',@text,'COLLECTION',false),
      (@tenantId,@loanId,@actorId,'Sistema de Cobrança','SYSTEM',@internal,'COLLECTION_INTERNAL',true);
update ged.loan_request set collection_count=coalesce(collection_count,0)+1,last_collection_at=now(),collection_level=@level where tenant_id=@tenantId and id=@loanId;
insert into ged.loan_request_history(tenant_id,loan_request_id,old_status,new_status,action,user_id,user_name,reason,internal_notes,metadata_json,correlation_id,reg_status)
values(@tenantId,@loanId,@status,@status,'LOAN_COLLECTION',@actorId,'Sistema de Cobrança',@text,'Envio externo pendente',jsonb_build_object('level',@level,'channel','INTERNAL'),gen_random_uuid()::text,'A');
""", new { tenantId, loanId, level, text, internal = $"Cobrança {level} registrada para acompanhamento do gestor.", actorId, status = loan.Status }, tx, cancellationToken: ct));
            var legacy = await conn.ExecuteScalarAsync<string?>(new CommandDefinition("select to_regclass('ged.loan_history')::text", transaction: tx, cancellationToken: ct));
            if (legacy is not null) await conn.ExecuteAsync(new CommandDefinition("insert into ged.loan_history(tenant_id,loan_id,event_time,event_type,by_user_id,notes,reg_date,reg_status) values(@tenantId,@loanId,now(),'COLLECTION',@actorId,@text,now(),'A')", new { tenantId, loanId, actorId, text }, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
            await audit.WriteAsync(tenantId, actorId, "LOAN_COLLECTION_REGISTERED", "loan_request", loanId, "Cobrança interna registrada", null, null, new { level, externalDelivery = "pending" }, ct);
            return Result.Ok();
        }
        catch (Exception ex) { logger.LogError(ex, "Falha ao registrar cobrança. Tenant={TenantId} Loan={LoanId}", tenantId, loanId); return Result.Fail("COLLECTION", "Não foi possível registrar a cobrança."); }
    }
}

public sealed class LoanReportService(IDbConnectionFactory db, IAuditWriter audit) : ILoanReportService
{
    public async Task<LoanReportResult> RunAsync(Guid tenantId, Guid actorId, LoanReportFilter filter, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        const string sql = """
select id as Id, protocol_no as ProtocolNo, coalesce(requester_name,'') as RequesterName,
coalesce(requester_sector_name,requester_sector) as Sector,status::text as Status,delivery_mode as DeliveryMode,
requested_at as RequestedAt,due_at as DueAt,returned_at as ReturnedAt,
greatest(0,extract(day from (coalesce(returned_at,now())-due_at)))::int as DaysLate,coalesce(collection_count,0) as CollectionCount
from ged.loan_request where tenant_id=@tenantId and coalesce(reg_status,'A')='A'
and (@from is null or requested_at>=@from) and (@to is null or requested_at<@to + interval '1 day')
and (@status is null or status::text=@status) and (@requester is null or requester_name ilike '%'||@requester||'%')
and (@sector is null or coalesce(requester_sector_name,requester_sector,'') ilike '%'||@sector||'%')
and (@mode is null or delivery_mode=@mode) and (@overdue is not true or due_at<now() and returned_at is null)
order by requested_at desc limit 5000;
""";
        var rows = (await conn.QueryAsync<LoanReportRow>(new CommandDefinition(sql, new { tenantId, from=filter.From, to=filter.To, status=Null(filter.Status), requester=Null(filter.Requester), sector=Null(filter.Sector), mode=Null(filter.DeliveryMode), overdue=filter.OverdueOnly }, cancellationToken: ct))).AsList();
        await conn.ExecuteAsync(new CommandDefinition("insert into ged.loan_report_run(tenant_id,run_by,filters_json,row_count) values(@tenantId,@actorId,@filters::jsonb,@count)", new { tenantId, actorId, filters=System.Text.Json.JsonSerializer.Serialize(filter), count=rows.Count }, cancellationToken: ct));
        await audit.WriteAsync(tenantId, actorId, "LOAN_REPORT_RUN", "loan_report_run", null, "Relatório operacional executado", null, null, new { rows=rows.Count }, ct);
        return new LoanReportResult { Rows=rows };
    }
    private static string? Null(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
