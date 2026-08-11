using Dapper;
using InovaGed.Application.Billing;
using InovaGed.Application.Common.Database;

namespace InovaGed.Infrastructure.Billing;

public sealed class BillingRuleService(IDbConnectionFactory db) : IBillingRuleService
{
    public async Task<IReadOnlyList<BillingExtractionRuleDto>> ListAsync(Guid tenantId, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        const string sql = """
select id "Id", name "Name", document_kind "DocumentKind", target_field "TargetField", keyword "Keyword",
 regex_pattern "RegexPattern", priority "Priority", is_required "IsRequired", is_active "IsActive",
 created_at "CreatedAt", updated_at "UpdatedAt"
from ged.billing_extraction_rule
where tenant_id=@tenantId and reg_status='A'
order by priority, name
""";
        return (await conn.QueryAsync<BillingExtractionRuleDto>(new CommandDefinition(sql, new { tenantId }, cancellationToken: ct))).AsList();
    }

    public async Task<Guid> SaveAsync(Guid tenantId, Guid userId, BillingExtractionRuleInput input, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var id = input.Id == Guid.Empty ? Guid.NewGuid() : input.Id;
        const string sql = """
insert into ged.billing_extraction_rule(id,tenant_id,name,document_kind,target_field,keyword,regex_pattern,priority,is_required,is_active,created_by)
values(@id,@tenantId,@Name,@DocumentKind,@TargetField,nullif(btrim(@Keyword),''),nullif(btrim(@RegexPattern),''),@Priority,@IsRequired,@IsActive,@userId)
on conflict(id) do update set name=excluded.name,document_kind=excluded.document_kind,target_field=excluded.target_field,
 keyword=excluded.keyword,regex_pattern=excluded.regex_pattern,priority=excluded.priority,is_required=excluded.is_required,
 is_active=excluded.is_active,updated_by=@userId,updated_at=now()
where ged.billing_extraction_rule.tenant_id=@tenantId and ged.billing_extraction_rule.reg_status='A'
""";
        await conn.ExecuteAsync(new CommandDefinition(sql, new { id, tenantId, userId, input.Name, input.DocumentKind, input.TargetField, input.Keyword, input.RegexPattern, input.Priority, input.IsRequired, input.IsActive }, cancellationToken: ct));
        return id;
    }

    public async Task<bool> DeleteAsync(Guid tenantId, Guid userId, Guid id, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition("update ged.billing_extraction_rule set reg_status='I',is_active=false,updated_by=@userId,updated_at=now() where tenant_id=@tenantId and id=@id and reg_status='A'", new { tenantId, userId, id }, cancellationToken: ct)) == 1;
    }
}
