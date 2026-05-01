using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Parameters;

namespace InovaGed.Infrastructure.Parameters;

public sealed class ParameterRepository : IParameterRepository
{
    private readonly IDbConnectionFactory _db;

    public ParameterRepository(IDbConnectionFactory db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ParameterCategoryRow>> ListCategoriesAsync(Guid tenantId, CancellationToken ct)
    {
        const string sql = @"
select
    c.id as Id,
    c.code as Code,
    c.name as Name,
    c.description as Description,
    c.icon as Icon,
    c.display_order as DisplayOrder,
    c.is_system as IsSystem,
    c.allow_hierarchy as AllowHierarchy,
    c.is_active as IsActive,
    count(i.id) filter (where i.reg_status='A')::int as TotalItems
from ged.parameter_category c
left join ged.parameter_item i on i.tenant_id=c.tenant_id and i.category_id=c.id
where c.tenant_id=@tenantId and c.reg_status='A'
group by c.id
order by c.display_order, c.name;";

        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<ParameterCategoryRow>(new CommandDefinition(sql, new { tenantId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<ParameterItemRow>> ListItemsAsync(Guid tenantId, string? categoryCode, string? search, CancellationToken ct)
    {
        const string sql = @"
select
    i.id as Id,
    i.category_id as CategoryId,
    i.parent_id as ParentId,
    c.code as CategoryCode,
    c.name as CategoryName,
    i.code as Code,
    i.name as Name,
    i.description as Description,
    i.abbreviation as Abbreviation,
    i.external_code as ExternalCode,
    i.color as Color,
    i.icon as Icon,
    p.name as ParentName,
    case when i.metadata_json is null then null else i.metadata_json::text end as MetadataJson,
    i.display_order as DisplayOrder,
    i.is_default as IsDefault,
    i.is_system as IsSystem,
    i.is_active as IsActive,
    i.created_at as CreatedAt,
    i.updated_at as UpdatedAt
from ged.parameter_item i
join ged.parameter_category c on c.id=i.category_id and c.tenant_id=i.tenant_id
left join ged.parameter_item p on p.id=i.parent_id and p.tenant_id=i.tenant_id
where i.tenant_id=@tenantId
  and i.reg_status='A'
  and c.reg_status='A'
  and (@categoryCode is null or c.code=@categoryCode)
  and (
      @search is null
      or unaccent(lower(i.code)) like unaccent(lower('%' || @search || '%'))
      or unaccent(lower(i.name)) like unaccent(lower('%' || @search || '%'))
      or unaccent(lower(coalesce(i.description,''))) like unaccent(lower('%' || @search || '%'))
  )
order by c.display_order, i.display_order, i.name;";

        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<ParameterItemRow>(new CommandDefinition(sql, new
        {
            tenantId,
            categoryCode = string.IsNullOrWhiteSpace(categoryCode) ? null : categoryCode.Trim().ToUpperInvariant(),
            search = string.IsNullOrWhiteSpace(search) ? null : search.Trim()
        }, cancellationToken: ct));
        return rows.ToList();
    }


    public async Task<IReadOnlyList<ParameterSelectOption>> ListOptionsAsync(Guid tenantId, IEnumerable<string> categoryCodes, CancellationToken ct)
    {
        var codes = categoryCodes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToUpperInvariant())
            .Distinct()
            .ToArray();

        if (codes.Length == 0)
            return Array.Empty<ParameterSelectOption>();

        const string sql = @"
select
    c.code as CategoryCode,
    case
        when c.code in ('NIVEL_SIGILO','SITUACAO_FUNCIONAL','EVENTO_TEMPORALIDADE','DESTINACAO_FINAL') then i.code
        else i.name
    end as Value,
    i.name as Text,
    i.description as Description,
    i.abbreviation as Abbreviation,
    i.external_code as ExternalCode,
    i.is_default as IsDefault,
    i.display_order as DisplayOrder
from ged.parameter_item i
join ged.parameter_category c on c.id=i.category_id and c.tenant_id=i.tenant_id
where i.tenant_id=@tenantId
  and i.reg_status='A'
  and i.is_active=true
  and c.reg_status='A'
  and c.is_active=true
  and c.code = any(@codes)
order by c.display_order, i.display_order, i.name;";

        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<ParameterSelectOption>(new CommandDefinition(sql, new { tenantId, codes }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<ParameterItemEditVM?> GetItemAsync(Guid tenantId, Guid id, CancellationToken ct)
    {
        const string sql = @"
select
    id as Id,
    category_id as CategoryId,
    parent_id as ParentId,
    code as Code,
    name as Name,
    description as Description,
    abbreviation as Abbreviation,
    external_code as ExternalCode,
    color as Color,
    icon as Icon,
    case when metadata_json is null then null else metadata_json::text end as MetadataJson,
    display_order as DisplayOrder,
    is_default as IsDefault,
    is_active as IsActive
from ged.parameter_item
where tenant_id=@tenantId and id=@id and reg_status='A'
limit 1;";

        await using var conn = await _db.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<ParameterItemEditVM>(new CommandDefinition(sql, new { tenantId, id }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ParameterItemRow>> ListParentOptionsAsync(Guid tenantId, Guid categoryId, Guid? ignoreId, CancellationToken ct)
    {
        const string sql = @"
select id as Id, category_id as CategoryId, code as Code, name as Name, display_order as DisplayOrder
from ged.parameter_item
where tenant_id=@tenantId
  and category_id=@categoryId
  and reg_status='A'
  and is_active=true
  and (@ignoreId is null or id <> @ignoreId)
order by display_order, name;";

        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<ParameterItemRow>(new CommandDefinition(sql, new { tenantId, categoryId, ignoreId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<Guid> UpsertItemAsync(Guid tenantId, Guid userId, ParameterItemEditVM vm, CancellationToken ct)
    {
        if (vm.CategoryId == Guid.Empty) throw new ArgumentException("Categoria obrigatória.");
        if (string.IsNullOrWhiteSpace(vm.Code)) throw new ArgumentException("Código obrigatório.");
        if (string.IsNullOrWhiteSpace(vm.Name)) throw new ArgumentException("Nome obrigatório.");
        if (!string.IsNullOrWhiteSpace(vm.MetadataJson))
        {
            try { _ = System.Text.Json.JsonDocument.Parse(vm.MetadataJson); }
            catch { throw new ArgumentException("JSON de metadados inválido."); }
        }

        var id = vm.Id ?? Guid.NewGuid();

        const string beforeSql = @"select to_jsonb(t) from (select * from ged.parameter_item where tenant_id=@tenantId and id=@id) t;";
        const string upsertSql = @"
insert into ged.parameter_item(
    id, tenant_id, category_id, parent_id, code, name, description, abbreviation,
    external_code, color, icon, metadata_json, display_order, is_default, is_active,
    created_at, created_by, updated_at, updated_by)
values(
    @id, @tenantId, @categoryId, @parentId, @code, @name, @description, @abbreviation,
    @externalCode, @color, @icon, cast(@metadataJson as jsonb), @displayOrder, @isDefault, @isActive,
    now(), @userId, now(), @userId)
on conflict (id) do update set
    category_id=excluded.category_id,
    parent_id=excluded.parent_id,
    code=excluded.code,
    name=excluded.name,
    description=excluded.description,
    abbreviation=excluded.abbreviation,
    external_code=excluded.external_code,
    color=excluded.color,
    icon=excluded.icon,
    metadata_json=excluded.metadata_json,
    display_order=excluded.display_order,
    is_default=excluded.is_default,
    is_active=excluded.is_active,
    updated_at=now(),
    updated_by=@userId,
    reg_status='A';";
        const string categorySql = "select code from ged.parameter_category where tenant_id=@tenantId and id=@categoryId";
        const string afterSql = @"select to_jsonb(t) from (select * from ged.parameter_item where tenant_id=@tenantId and id=@id) t;";
        const string historySql = @"
insert into ged.parameter_item_history(tenant_id, item_id, category_code, action, changed_by, old_data, new_data)
values(@tenantId, @id, @categoryCode, @action, @userId, cast(@oldData as jsonb), cast(@newData as jsonb));";

        await using var conn = await _db.OpenAsync(ct);
        using var tx = conn.BeginTransaction();

        var oldData = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(beforeSql, new { tenantId, id }, tx, cancellationToken: ct));
        var categoryCode = await conn.ExecuteScalarAsync<string>(new CommandDefinition(categorySql, new { tenantId, categoryId = vm.CategoryId }, tx, cancellationToken: ct));
        if (string.IsNullOrWhiteSpace(categoryCode)) throw new ArgumentException("Categoria não encontrada.");

        await conn.ExecuteAsync(new CommandDefinition(upsertSql, new
        {
            id,
            tenantId,
            categoryId = vm.CategoryId,
            parentId = vm.ParentId,
            code = vm.Code.Trim().ToUpperInvariant(),
            name = vm.Name.Trim(),
            description = vm.Description,
            abbreviation = vm.Abbreviation,
            externalCode = vm.ExternalCode,
            color = vm.Color,
            icon = vm.Icon,
            metadataJson = string.IsNullOrWhiteSpace(vm.MetadataJson) ? null : vm.MetadataJson,
            displayOrder = vm.DisplayOrder,
            isDefault = vm.IsDefault,
            isActive = vm.IsActive,
            userId
        }, tx, cancellationToken: ct));

        if (vm.IsDefault)
        {
            await conn.ExecuteAsync(new CommandDefinition(@"
update ged.parameter_item
set is_default=false, updated_at=now(), updated_by=@userId
where tenant_id=@tenantId and category_id=@categoryId and id<>@id and reg_status='A';",
                new { tenantId, categoryId = vm.CategoryId, id, userId }, tx, cancellationToken: ct));
        }

        var newData = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(afterSql, new { tenantId, id }, tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(historySql, new
        {
            tenantId,
            id,
            categoryCode,
            action = oldData is null ? "CREATE" : "UPDATE",
            userId,
            oldData,
            newData
        }, tx, cancellationToken: ct));

        tx.Commit();
        return id;
    }

    public async Task SetActiveAsync(Guid tenantId, Guid userId, Guid id, bool active, CancellationToken ct)
    {
        const string sql = @"
update ged.parameter_item
set is_active=@active, updated_at=now(), updated_by=@userId
where tenant_id=@tenantId and id=@id and reg_status='A';";
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, userId, id, active }, cancellationToken: ct));
    }

    public async Task DeleteAsync(Guid tenantId, Guid userId, Guid id, string? reason, CancellationToken ct)
    {
        const string beforeSql = @"select to_jsonb(t) from (select i.*, c.code category_code from ged.parameter_item i join ged.parameter_category c on c.id=i.category_id where i.tenant_id=@tenantId and i.id=@id) t;";
        const string deleteSql = @"
update ged.parameter_item
set reg_status='I', is_active=false, updated_at=now(), updated_by=@userId
where tenant_id=@tenantId and id=@id and reg_status='A' and is_system=false;";
        const string historySql = @"
insert into ged.parameter_item_history(tenant_id, item_id, category_code, action, changed_by, old_data, reason)
select @tenantId, @id, coalesce(c.code,'UNKNOWN'), 'DELETE', @userId, cast(@oldData as jsonb), @reason
from ged.parameter_item i
left join ged.parameter_category c on c.id=i.category_id
where i.tenant_id=@tenantId and i.id=@id;";

        await using var conn = await _db.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        var oldData = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(beforeSql, new { tenantId, id }, tx, cancellationToken: ct));
        var rows = await conn.ExecuteAsync(new CommandDefinition(deleteSql, new { tenantId, userId, id }, tx, cancellationToken: ct));
        if (rows == 0) throw new InvalidOperationException("Parâmetro não encontrado ou é item de sistema e não pode ser excluído.");
        await conn.ExecuteAsync(new CommandDefinition(historySql, new { tenantId, id, userId, oldData, reason }, tx, cancellationToken: ct));
        tx.Commit();
    }
}
