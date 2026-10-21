using System.ComponentModel.DataAnnotations;
using Dapper;
using InovaGed.Application.Billing;
using InovaGed.Application.Common.Database;

namespace InovaGed.Infrastructure.Billing;

public sealed class BillingOperations(IDbConnectionFactory db) : IBillingOperations
{
    public async Task<BillingDuplicatePolicy> DuplicatePolicyAsync(Guid tenantId, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<BillingDuplicatePolicy>(new CommandDefinition("select compare_series as \"CompareSeries\",compare_competence as \"CompareCompetence\",compare_amount as \"CompareAmount\" from ged.billing_duplicate_policy where tenant_id=@tenantId",new{tenantId},cancellationToken:ct)) ?? new();
    }

    public async Task SaveDuplicatePolicyAsync(Guid tenantId, Guid userId, BillingDuplicatePolicy policy, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
insert into ged.billing_duplicate_policy(tenant_id,compare_series,compare_competence,compare_amount,updated_by)
values(@tenantId,@CompareSeries,@CompareCompetence,@CompareAmount,@userId)
on conflict(tenant_id) do update set compare_series=excluded.compare_series,compare_competence=excluded.compare_competence,
compare_amount=excluded.compare_amount,updated_by=excluded.updated_by,updated_at=now()
""",new{tenantId,userId,policy.CompareSeries,policy.CompareCompetence,policy.CompareAmount},cancellationToken:ct));
    }

    public async Task<IReadOnlyList<BillingAssignee>> AssigneesAsync(Guid tenantId, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return (await conn.QueryAsync<BillingAssignee>(new CommandDefinition("select id as Id,name as Name from ged.app_user where tenant_id=@tenantId and is_active and reg_status='A' order by name,id", new { tenantId }, cancellationToken: ct))).AsList();
    }

    public async Task<BillingOperationsDto> GetAsync(Guid tenantId, Guid extractionId, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var args = new { tenantId, extractionId };
        var installments = (await conn.QueryAsync<BillingInstallmentDto>(new CommandDefinition("""
select i.id "Id",i.sequence "Sequence",i.direction "Direction",i.due_date "DueDate",i.amount "Amount",
coalesce((select sum(s.amount) from ged.billing_settlement s where s.tenant_id=i.tenant_id and s.installment_id=i.id),0) "Settled"
from ged.billing_installment i where i.tenant_id=@tenantId and i.extraction_id=@extractionId order by i.sequence
""", args, cancellationToken: ct))).AsList();
        var settlements = (await conn.QueryAsync<BillingSettlementDto>(new CommandDefinition("""
select s.id "Id",i.sequence "Sequence",s.amount "Amount",s.settled_on "SettledOn",s.reference "Reference",s.reconciled_at "ReconciledAt",s.created_by "CreatedBy"
from ged.billing_settlement s join ged.billing_installment i on i.tenant_id=s.tenant_id and i.id=s.installment_id
where i.tenant_id=@tenantId and i.extraction_id=@extractionId order by s.created_at,s.id
""", args, cancellationToken: ct))).AsList();
        var history = (await conn.QueryAsync<BillingHistoryDto>(new CommandDefinition("""
select reviewed_at "ReviewedAt",reviewed_by "ReviewedBy",reason "Reason",snapshot_json::text "SnapshotJson",previous_snapshot_json::text "PreviousSnapshotJson"
from ged.billing_extraction_review_history where tenant_id=@tenantId and extraction_id=@extractionId order by reviewed_at desc,id desc
""", args, cancellationToken: ct))).AsList();
        return new(installments, settlements, history);
    }

    public async Task<bool> AddInstallmentAsync(Guid tenantId, Guid userId, BillingInstallmentInput input, CancellationToken ct)
    {
        if (!Valid(input) || input.ExtractionId == Guid.Empty || decimal.Round(input.Amount, 2) != input.Amount) return false;
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var total = await conn.QuerySingleOrDefaultAsync<decimal?>(new CommandDefinition("""
select coalesce(net_amount,gross_amount) from ged.billing_document_extraction
where tenant_id=@tenantId and id=@ExtractionId and reg_status='A' and extraction_status='APPROVED' for update
""", new { tenantId, input.ExtractionId }, tx, cancellationToken: ct));
        if (total is null) return false;
        var count = await conn.ExecuteScalarAsync<int>(new CommandDefinition("""
insert into ged.billing_installment(tenant_id,extraction_id,sequence,direction,due_date,amount,created_by)
select @tenantId,@ExtractionId,@Sequence,@Direction,@DueDate,@Amount,@userId
where @Amount+coalesce((select sum(amount) from ged.billing_installment where tenant_id=@tenantId and extraction_id=@ExtractionId),0)<=@total
and not exists(select 1 from ged.billing_installment where tenant_id=@tenantId and extraction_id=@ExtractionId and direction<>@Direction)
on conflict(tenant_id,extraction_id,sequence) do nothing returning 1
""", new { tenantId, userId, input.ExtractionId, input.Sequence, input.Direction, input.DueDate, input.Amount, total }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return count == 1;
    }

    public async Task<bool> SettleAsync(Guid tenantId, Guid userId, BillingSettlementInput input, CancellationToken ct)
    {
        if (!Valid(input) || input.IdempotencyKey == Guid.Empty || decimal.Round(input.Amount, 2) != input.Amount) return false;
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        // Lock the extraction before its installment: reviews and financial changes serialize together.
        var approved = await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
select extraction_status='APPROVED' from ged.billing_document_extraction
where tenant_id=@tenantId and id=@ExtractionId and reg_status='A' for update
""", new { tenantId, input.ExtractionId }, tx, cancellationToken: ct));
        if (!approved) return false;
        var amount = await conn.QuerySingleOrDefaultAsync<decimal?>(new CommandDefinition("select amount from ged.billing_installment where tenant_id=@tenantId and extraction_id=@ExtractionId and id=@InstallmentId for update", new { tenantId, input.ExtractionId, input.InstallmentId }, tx, cancellationToken: ct));
        if (amount is null) return false;
        var replay = await conn.QuerySingleOrDefaultAsync<SettlementReplay>(new CommandDefinition("select installment_id as \"InstallmentId\",amount as \"Amount\",settled_on as \"SettledOn\",reference as \"Reference\" from ged.billing_settlement where tenant_id=@tenantId and idempotency_key=@IdempotencyKey", new { tenantId, input.IdempotencyKey }, tx, cancellationToken: ct));
        if (replay is not null) return replay.InstallmentId == input.InstallmentId && replay.Amount == input.Amount && replay.SettledOn.Date == input.SettledOn!.Value.Date && replay.Reference == input.Reference.Trim();
        var applied = await conn.ExecuteScalarAsync<int>(new CommandDefinition("""
insert into ged.billing_settlement(tenant_id,installment_id,amount,settled_on,reference,idempotency_key,created_by)
select @tenantId,@InstallmentId,@Amount,@SettledOn,@reference,@IdempotencyKey,@userId
where @Amount+coalesce((select sum(amount) from ged.billing_settlement where tenant_id=@tenantId and installment_id=@InstallmentId),0)<=@limitAmount
on conflict(tenant_id,idempotency_key) do nothing returning 1
""", new { tenantId, userId, input.InstallmentId, input.Amount, input.SettledOn, reference = input.Reference.Trim(), input.IdempotencyKey, limitAmount = amount }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return applied == 1;
    }

    public async Task<bool> ReconcileAsync(Guid tenantId, Guid userId, Guid settlementId, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition("update ged.billing_settlement set reconciled_by=@userId,reconciled_at=now() where tenant_id=@tenantId and id=@settlementId and reconciled_at is null", new { tenantId, userId, settlementId }, cancellationToken: ct)) == 1;
    }

    private static bool Valid(object input) => Validator.TryValidateObject(input, new ValidationContext(input), new List<ValidationResult>(), true);
    private sealed class SettlementReplay { public Guid InstallmentId { get; set; } public decimal Amount { get; set; } public DateTime SettledOn { get; set; } public string Reference { get; set; } = ""; }
}
