using Dapper;
using InovaGed.Application.Common.Database;
using Npgsql;
using System.Data;

public sealed class InstrumentVersionRepository
{
    private readonly IDbConnectionFactory _db;
    public InstrumentVersionRepository(IDbConnectionFactory db) => _db = db;

    // Lista histórico (PCD/TTD/POP)
    public async Task<IEnumerable<InstrumentVersionRow>> ListAsync(Guid tenantId, string instrumentType, CancellationToken ct)
    {
        using var conn = await _db.OpenAsync(ct);

        // instrument_type no teu banco é enum ged.instrument_type ('PCD','TTD','POP')
        var sql = @"
select
  v.id,
  v.tenant_id,
  v.instrument_type::text as instrument_type,
  v.version_no,
  v.is_published,
  v.published_at,
  v.published_by,
  u.name as published_by_name,
  v.notes
from ged.instrument_version v
left join ged.app_user u on u.id = v.published_by
where v.tenant_id = @tenantId
  and v.instrument_type::text = @instrumentType
order by v.version_no desc, v.published_at desc nulls last;";

        return await conn.QueryAsync<InstrumentVersionRow>(sql, new { tenantId, instrumentType });
    }

    // Publica uma nova versão: "snapshot" do instrumento + itens (PCD/TTD/POP)
    public async Task<Guid> PublishAsync(Guid tenantId, string instrumentType, Guid publishedBy, string notes, CancellationToken ct)
    {
        using var conn = await _db.OpenAsync(ct);
        using var tx = conn.BeginTransaction();

        // Próximo version_no
        var nextNo = await conn.ExecuteScalarAsync<int>(@"
select coalesce(max(version_no), 0) + 1
from ged.instrument_version
where tenant_id=@tenantId and instrument_type::text=@instrumentType;", new { tenantId, instrumentType }, tx);

        var versionId = Guid.NewGuid();

        // instrument_version (header)
        await conn.ExecuteAsync(@"
insert into ged.instrument_version
(id, tenant_id, instrument_type, version_no, is_published, published_at, published_by, notes, reg_date, reg_status)
values
(@id, @tenantId, @instrumentType::ged.instrument_type, @versionNo, true, now(), @publishedBy, @notes, now(), 'A');",
        new { id = versionId, tenantId, instrumentType, versionNo = nextNo, publishedBy, notes }, tx);

        // Snapshot por tipo:
        // - PCD/TTD normalmente estão em class_node / retention_rule / classification_plan*
        // Para PoC: copie as classes (class_node) + regras (retention_rule) para tabelas de snapshot já existentes
        // Se no teu banco existir "classification_plan_version_item", você pode usar esse snapshot para PCD/TTD.
        if (instrumentType is "PCD" or "TTD")
        {
            // Snapshot da árvore de classificação (class_node) no formato "version_item"
            await conn.ExecuteAsync(@"
insert into ged.classification_plan_version_item
(id, tenant_id, version_id, node_id, code, title, parent_code, sort_order, reg_date, reg_status)
select
  gen_random_uuid(), cn.tenant_id, @versionId,
  cn.id, cn.code, cn.title, cn.parent_code, cn.sort_order, now(), 'A'
from ged.class_node cn
where cn.tenant_id=@tenantId and cn.reg_status='A';",
            new { versionId, tenantId }, tx);
        }
        else if (instrumentType == "POP")
        {
            // POP: snapshot de procedimentos (exemplo: tabela pop_procedure_version já existe no dump)
            await conn.ExecuteAsync(@"
insert into ged.pop_procedure_version
(id, tenant_id, version_id, title, content, reg_date, reg_status)
select
  gen_random_uuid(), p.tenant_id, @versionId, p.title, p.content, now(), 'A'
from ged.pop_procedure p
where p.tenant_id=@tenantId and p.reg_status='A';",
            new { versionId, tenantId }, tx);
        }

        tx.Commit();
        return versionId;
    }

    // Diff simples: o que entrou/saiu/alterou entre duas versões
    public async Task<InstrumentDiffResult> DiffAsync(Guid tenantId, Guid fromVersionId, Guid toVersionId, CancellationToken ct)
    {
        using var conn = await _db.OpenAsync(ct);

        // Exemplo focado em classificação (PCD/TTD):
        var sql = @"
with a as (
  select code, title, parent_code, sort_order
  from ged.classification_plan_version_item
  where tenant_id=@tenantId and version_id=@fromVersionId and reg_status='A'
),
b as (
  select code, title, parent_code, sort_order
  from ged.classification_plan_version_item
  where tenant_id=@tenantId and version_id=@toVersionId and reg_status='A'
)
select 'ADDED' as change, b.*
from b left join a on a.code=b.code
where a.code is null

union all
select 'REMOVED' as change, a.*
from a left join b on b.code=a.code
where b.code is null

union all
select 'UPDATED' as change, b.*
from b join a on a.code=b.code
where (a.title, a.parent_code, a.sort_order) is distinct from (b.title, b.parent_code, b.sort_order)

order by change, code;";

        var rows = (await conn.QueryAsync<InstrumentDiffRow>(sql, new { tenantId, fromVersionId, toVersionId })).ToList();
        return new InstrumentDiffResult(rows);
    }
}

public sealed record InstrumentVersionRow(
    Guid Id,
    Guid Tenant_Id,
    string Instrument_Type,
    int Version_No,
    bool Is_Published,
    DateTimeOffset? Published_At,
    Guid? Published_By,
    string? Published_By_Name,
    string? Notes);

public sealed record InstrumentDiffRow(string Change, string Code, string Title, string? Parent_Code, int Sort_Order);

public sealed record InstrumentDiffResult(IReadOnlyList<InstrumentDiffRow> Rows);