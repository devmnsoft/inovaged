using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.PhysicalArchive;
using InovaGed.Application.Labels.Intelligence;

namespace InovaGed.Infrastructure.PhysicalArchive;

public sealed class LabelPrintRegistrar(IDbConnectionFactory dbFactory, ILabelCustodyService custody) : ILabelPrintRegistrar, ILabelPrintService
{
    public async Task RegisterAsync(LabelPrintRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SnapshotJson);
        if (request.TenantId == Guid.Empty || request.UserId == Guid.Empty)
            throw new InvalidOperationException("Tenant e usuário autenticado são obrigatórios para registrar a impressão.");

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.SnapshotJson))).ToLowerInvariant();
        await using var db = await dbFactory.OpenAsync(cancellationToken);
        await using var tx = await db.BeginTransactionAsync(cancellationToken);
        var priorPrints = await db.ExecuteScalarAsync<int>(new CommandDefinition("""
select count(*) from ged.label_print_history
where tenant_id=@TenantId and label_subject_type=@SubjectType and label_subject_id=@SubjectId and template_code=@TemplateCode;
""", request, tx, cancellationToken: cancellationToken));
        if (priorPrints > 0 && string.IsNullOrWhiteSpace(request.ReprintReason))
            throw new InvalidOperationException("O motivo da reimpressão é obrigatório.");
        var boxId = request.SubjectType.Equals("BOX", StringComparison.OrdinalIgnoreCase) ? request.SubjectId : (Guid?)null;
        var documentId = request.SubjectType.Equals("DOCUMENT", StringComparison.OrdinalIgnoreCase) ? request.SubjectId : (Guid?)null;
        await db.ExecuteAsync(new CommandDefinition("""
insert into ged.label_print
 (id, tenant_id, box_id, document_id, label_type, printed_by, ip_address, user_agent, data,
  snapshot_json, payload_hash_sha256, template_version, reprint_reason, print_channel, reg_status)
values
 (gen_random_uuid(), @TenantId, @BoxId, @DocumentId, @SubjectType, @UserId, cast(@IpAddress as inet),
  @UserAgent, cast(@SnapshotJson as jsonb), cast(@SnapshotJson as jsonb), @Hash, @TemplateCode,
  nullif(@ReprintReason, ''), 'WEB', 'A');
""", new { request.TenantId, BoxId = boxId, DocumentId = documentId, request.SubjectType, request.UserId,
            request.IpAddress, request.UserAgent, request.SnapshotJson, Hash = hash, request.TemplateCode, request.ReprintReason },
            tx, cancellationToken: cancellationToken));
        await db.ExecuteAsync(new CommandDefinition("""
insert into ged.label_print_history
 (id, tenant_id, label_subject_type, label_subject_id, template_code, snapshot_json,
  snapshot_sha256, printed_by, ip_address, user_agent, reprint_reason)
values
 (gen_random_uuid(), @TenantId, @SubjectType, @SubjectId, @TemplateCode, cast(@SnapshotJson as jsonb),
  @Hash, @UserId, cast(@IpAddress as inet), @UserAgent, nullif(@ReprintReason, ''));
""", new { request.TenantId, request.SubjectType, request.SubjectId, request.TemplateCode, request.SnapshotJson, Hash = hash, request.UserId, request.IpAddress, request.UserAgent, request.ReprintReason }, tx, cancellationToken: cancellationToken));
        await tx.CommitAsync(cancellationToken);
        await custody.RegisterEventAsync(new(request.TenantId,request.SubjectType,request.SubjectId,null,
            priorPrints>0?"LABEL_REPRINTED":"LABEL_PRINTED",priorPrints>0?"Etiqueta reimpressa":"Etiqueta impressa",
            request.ReprintReason,"label_print_history",null,null,null,request.UserId,request.IpAddress,request.UserAgent,request.SnapshotJson),cancellationToken);
    }
}

public sealed class LabelPayloadBuilder : ILabelPayloadBuilder
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public string Build(object snapshot) => JsonSerializer.Serialize(snapshot, Options);
}

public sealed class LabelTemplateService : ILabelTemplateService
{
    private static readonly IReadOnlyDictionary<string, LabelTemplate> Templates =
        new Dictionary<string, LabelTemplate>(StringComparer.OrdinalIgnoreCase)
        {
            ["BOX"] = new("BOX_ATLAS", "2", "BOX"),
            ["DOCUMENT"] = new("DOCUMENT_ATLAS", "2", "DOCUMENT"),
            ["BATCH"] = new("BATCH_ATLAS", "2", "BATCH"),
            ["LOCDESK_FOLDER"] = new("LOCDESK_PASTA", "1", "DOCUMENT"),
            ["LOCDESK_BOX"] = new("LOCDESK_CAIXA", "1", "BOX")
        };

    public LabelTemplate GetCurrent(string subjectType) =>
        Templates.TryGetValue(subjectType, out var template)
            ? template
            : throw new ArgumentOutOfRangeException(nameof(subjectType), "Tipo de etiqueta não suportado.");
}
