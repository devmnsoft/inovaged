using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Loans;
using InovaGed.Domain.Primitives;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged.Loans;

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
