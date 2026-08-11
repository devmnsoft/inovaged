using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Labels;

namespace InovaGed.Infrastructure.Ged.Labels;
public sealed class LabelQueries(IDbConnectionFactory db) : ILabelQueries
{
    public async Task<IReadOnlyList<LabelRowDto>> ListAsync(Guid tenantId, string? search, string? type, string? status, CancellationToken ct)
    {
        await using var cn = await db.OpenAsync(ct);
        const string sql = """
select l.id "Id", l.label_code "LabelCode", l.title "Title", l.label_type "LabelType", l.status "Status",
 l.box_id "BoxId", b.box_no::text "BoxNo", coalesce(p.full_location_code,p.location_code) "Location",
 l.created_at "CreatedAt", (select max(h.printed_at) from ged.label_print_history h where h.tenant_id=l.tenant_id and h.label_subject_id=l.id) "LastPrintedAt"
from ged.physical_label l left join ged.box b on b.tenant_id=l.tenant_id and b.id=l.box_id
left join ged.physical_location p on p.tenant_id=l.tenant_id and p.id=coalesce(l.location_id,b.location_id)
where l.tenant_id=@tenantId and l.reg_status='A' and (@type='' or l.label_type=@type) and (@status='' or l.status=@status)
and (@search='' or l.label_code ilike '%'||@search||'%' or l.title ilike '%'||@search||'%' or coalesce(b.box_no::text,'') ilike '%'||@search||'%' or coalesce(p.full_location_code,p.location_code,'') ilike '%'||@search||'%')
order by l.updated_at desc nulls last, l.created_at desc
""";
        var rows = await cn.QueryAsync<LabelRowDto>(new CommandDefinition(sql, new { tenantId, search = search?.Trim() ?? "", type = type ?? "", status = status ?? "" }, cancellationToken: ct));
        return rows.AsList();
    }
    public async Task<LabelFormDto?> GetAsync(Guid tenantId, Guid id, CancellationToken ct)
    {
        await using var cn = await db.OpenAsync(ct);
        return await cn.QuerySingleOrDefaultAsync<LabelFormDto>(new CommandDefinition("select id \"Id\", label_code \"LabelCode\", title \"Title\", label_type \"LabelType\", box_id \"BoxId\", location_id \"LocationId\", description \"Description\", qr_payload \"QrPayload\", width_mm \"WidthMm\", height_mm \"HeightMm\", status \"Status\" from ged.physical_label where tenant_id=@tenantId and id=@id and reg_status='A'", new { tenantId, id }, cancellationToken: ct));
    }
}
