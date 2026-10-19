using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Infrastructure.Common.Dapper;
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
        var schema = await GetInstrumentVersionSchemaAsync(conn, ct);

        var isPublishedExpr = schema.HasIsPublished ? "v.is_published" : schema.HasPublishedAt ? "(v.published_at is not null)" : "false";
        var publishedAtExpr = schema.HasPublishedAt ? "v.published_at" : "null::timestamptz";
        var publishedByExpr = schema.HasPublishedBy ? "v.published_by" : "null::uuid";
        var notesExpr = schema.HasNotes ? "v.notes" : "null::text";
        var regStatusFilter = schema.HasRegStatus ? "and coalesce(v.reg_status,'A')='A'" : string.Empty;

        // instrument_type no teu banco é enum ged.instrument_type ('PCD','TTD','POP')
        var sql = $"""
select
  v.id as "Id",
  v.tenant_id as "TenantId",
  v.instrument_type::text as "InstrumentType",
  v.version_no as "VersionNo",
  {isPublishedExpr} as "IsPublished",
  {publishedAtExpr} as "PublishedAt",
  {publishedByExpr} as "PublishedBy",
  u.name as "PublishedByName",
  {notesExpr} as "Notes"
from ged.instrument_version v
left join ged.app_user u on u.id = {publishedByExpr}
where v.tenant_id = @tenantId
  and v.instrument_type::text = @instrumentType
  {regStatusFilter}
order by v.version_no desc, {publishedAtExpr} desc nulls last
""";

        var rows = await conn.QueryAsync<InstrumentVersionDbRow>(new CommandDefinition(sql, new { tenantId, instrumentType }, cancellationToken: ct));
        return rows.Select(x => new InstrumentVersionRow(
            x.Id, x.TenantId,
            string.IsNullOrWhiteSpace(x.InstrumentType) ? instrumentType : x.InstrumentType,
            x.VersionNo, x.IsPublished, DapperValueConverters.ToDateTimeOffset(x.PublishedAt), x.PublishedBy,
            x.PublishedByName, x.Notes)).ToList();
    }

    // Publica uma nova versão: "snapshot" do instrumento + itens (PCD/TTD/POP)
    public async Task<Guid> PublishAsync(Guid tenantId, string instrumentType, Guid publishedBy, string notes, CancellationToken ct)
    {
        using var conn = await _db.OpenAsync(ct);
        var schema = await GetInstrumentVersionSchemaAsync(conn, ct);
        if (!schema.TableExists || !schema.HasIsPublished || !schema.HasPublishedAt || !schema.HasPublishedBy || !schema.HasNotes || !schema.HasRegStatus || !schema.HasRegDate)
            throw new InvalidOperationException("Tabela ged.instrument_version ausente ou incompatível. Execute database/migrations/2026_08_21_instrument_version_compat_hotfix.sql ou database/apply_all_required_migrations.sql.");
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

        tx.Commit();
        return versionId;
    }

    public async Task<Guid> PublishPlanSnapshotAsync(Guid tenantId, Guid publishedBy, string title, string notes, CancellationToken ct)
    {
        using var conn = await _db.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        var nextNo = await conn.ExecuteScalarAsync<int>(
            "select coalesce(max(version_no), 0) + 1 from ged.classification_plan_version where tenant_id=@tenantId;",
            new { tenantId }, tx);
        var versionId = Guid.NewGuid();
        await conn.ExecuteAsync(@"
insert into ged.classification_plan_version(id, tenant_id, version_no, title, notes, published_at, published_by)
values (@id, @tenantId, @versionNo, @title, nullif(@notes,''), now(), @publishedBy);",
            new
            {
                id = versionId,
                tenantId,
                versionNo = nextNo,
                title,
                notes,
                publishedBy = publishedBy == Guid.Empty ? (Guid?)null : publishedBy
            }, tx);
        await conn.ExecuteAsync(@"
insert into ged.classification_plan_version_item(
    tenant_id, version_id, classification_id,
    code, name, description, parent_code,
    retention_start_event,
    retention_active_days, retention_active_months, retention_active_years,
    retention_archive_days, retention_archive_months, retention_archive_years,
    final_destination, requires_digital_signature,
    is_confidential, is_active, retention_notes)
select
    cp.tenant_id, @versionId, cp.id,
    cp.code, cp.name, cp.description, parent.code,
    cp.retention_start_event,
    cp.retention_active_days, cp.retention_active_months, cp.retention_active_years,
    cp.retention_archive_days, cp.retention_archive_months, cp.retention_archive_years,
    cp.final_destination,
    cp.requires_digital_signature,
    cp.is_confidential, cp.is_active, cp.retention_notes
from ged.classification_plan cp
left join ged.classification_plan parent
  on parent.tenant_id = cp.tenant_id and parent.id = cp.parent_id
where cp.tenant_id = @tenantId;",
            new { tenantId, versionId }, tx);
        tx.Commit();
        return versionId;
    }

    // Diff simples: o que entrou/saiu/alterou entre duas versões
    public async Task<InstrumentDiffResult> DiffAsync(Guid tenantId, Guid fromVersionId, Guid toVersionId, CancellationToken ct)
    {
        using var conn = await _db.OpenAsync(ct);
        var columns = (await conn.QueryAsync<string>(new CommandDefinition(
            "select column_name from information_schema.columns where table_schema='ged' and table_name='classification_plan_version_item'",
            cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!columns.Contains("code") || !columns.Contains("version_id"))
            return new InstrumentDiffResult(Array.Empty<InstrumentDiffRow>());

        var title = columns.Contains("name") ? "name" : columns.Contains("title") ? "title" : "code";
        var parent = columns.Contains("parent_code") ? "parent_code" : "null::text";
        var sort = columns.Contains("sort_order") ? "sort_order" : columns.Contains("display_order") ? "display_order" : "0";
        var reg = columns.Contains("reg_status") ? "and reg_status = 'A'" : string.Empty;
        var sql = $"""
with a as (
    select code, {title} as title, {parent} as parent_code, {sort} as sort_order
    from ged.classification_plan_version_item
    where tenant_id = @tenantId and version_id = @fromVersionId {reg}
),
b as (
    select code, {title} as title, {parent} as parent_code, {sort} as sort_order
    from ged.classification_plan_version_item
    where tenant_id = @tenantId and version_id = @toVersionId {reg}
)
select 'ADDED' as "Change", b.code as "Code", b.title as "Title", b.parent_code as "ParentCode", b.sort_order as "SortOrder"
from b left join a on a.code = b.code where a.code is null
union all
select 'REMOVED' as "Change", a.code as "Code", a.title as "Title", a.parent_code as "ParentCode", a.sort_order as "SortOrder"
from a left join b on b.code = a.code where b.code is null
union all
select 'UPDATED' as "Change", b.code as "Code", b.title as "Title", b.parent_code as "ParentCode", b.sort_order as "SortOrder"
from b join a on a.code = b.code
where (a.title, a.parent_code, a.sort_order) is distinct from (b.title, b.parent_code, b.sort_order)
order by "Change", "Code"
""";

        var rows = await conn.QueryAsync<InstrumentDiffDbRow>(new CommandDefinition(
            sql, new { tenantId, fromVersionId, toVersionId }, cancellationToken: ct));
        return new InstrumentDiffResult(rows.Select(x => new InstrumentDiffRow(
            x.Change ?? string.Empty, x.Code ?? string.Empty, x.Title ?? string.Empty,
            x.ParentCode, x.SortOrder)).ToList());
    }

    private static async Task<InstrumentVersionSchema> GetInstrumentVersionSchemaAsync(IDbConnection conn, CancellationToken ct)
    {
        const string sql = """
select column_name
from information_schema.columns
where table_schema = 'ged'
  and table_name = 'instrument_version'
""";
        var columns = (await conn.QueryAsync<string>(new CommandDefinition(sql, cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new InstrumentVersionSchema(columns.Count > 0, columns.Contains("is_published"), columns.Contains("published_at"), columns.Contains("published_by"), columns.Contains("notes"), columns.Contains("reg_status"), columns.Contains("reg_date"));
    }

    private sealed record InstrumentVersionSchema(bool TableExists, bool HasIsPublished, bool HasPublishedAt, bool HasPublishedBy, bool HasNotes, bool HasRegStatus, bool HasRegDate);

    private sealed class InstrumentVersionDbRow
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string? InstrumentType { get; set; }
        public int VersionNo { get; set; }
        public bool IsPublished { get; set; }
        public DateTime? PublishedAt { get; set; }
        public Guid? PublishedBy { get; set; }
        public string? PublishedByName { get; set; }
        public string? Notes { get; set; }
    }

    private sealed class InstrumentDiffDbRow
    {
        public string? Change { get; set; }
        public string? Code { get; set; }
        public string? Title { get; set; }
        public string? ParentCode { get; set; }
        public int SortOrder { get; set; }
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
