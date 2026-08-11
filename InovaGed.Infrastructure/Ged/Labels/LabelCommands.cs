using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Labels;

namespace InovaGed.Infrastructure.Ged.Labels;
public sealed class LabelCommands(IDbConnectionFactory db) : ILabelCommands
{
    public async Task<Guid> SaveAsync(Guid tenantId, Guid userId, LabelFormDto l, CancellationToken ct)
    {
        await using var cn = await db.OpenAsync(ct);
        var id = l.Id.GetValueOrDefault(Guid.NewGuid());
        const string sql = """
insert into ged.physical_label(id,tenant_id,label_code,title,label_type,box_id,location_id,description,qr_payload,width_mm,height_mm,status,created_by)
values(@id,@tenantId,@LabelCode,@Title,@LabelType,@BoxId,@LocationId,@Description,@QrPayload,@WidthMm,@HeightMm,@Status,@userId)
on conflict(id) do update set label_code=excluded.label_code,title=excluded.title,label_type=excluded.label_type,box_id=excluded.box_id,
location_id=excluded.location_id,description=excluded.description,qr_payload=excluded.qr_payload,width_mm=excluded.width_mm,height_mm=excluded.height_mm,status=excluded.status,updated_by=@userId,updated_at=now()
where physical_label.tenant_id=@tenantId
""";
        await cn.ExecuteAsync(new CommandDefinition(sql, new { id, tenantId, userId, l.LabelCode, l.Title, l.LabelType, l.BoxId, l.LocationId, l.Description, l.QrPayload, l.WidthMm, l.HeightMm, l.Status }, cancellationToken: ct));
        return id;
    }
    public async Task InactivateAsync(Guid tenantId, Guid userId, Guid id, CancellationToken ct)
    {
        await using var cn = await db.OpenAsync(ct);
        await cn.ExecuteAsync(new CommandDefinition("update ged.physical_label set status='INACTIVE',reg_status='I',updated_by=@userId,updated_at=now() where tenant_id=@tenantId and id=@id", new { tenantId, userId, id }, cancellationToken: ct));
    }
}
