using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Labels;
using InovaGed.Application.Labels.Intelligence;
using InovaGed.Application.PhysicalArchive;
using InovaGed.Infrastructure.Common.Database;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.PhysicalArchive;

public sealed class LabelPrintRegistrar(
    IDbConnectionFactory dbFactory,
    ILabelCustodyService custody,
    ILabelTraceabilityService traceability,
    ILogger<LabelPrintRegistrar> logger) : ILabelPrintRegistrar, ILabelPrintService
{
    private static readonly DatabaseSchemaReader Schema = new();
    private static readonly string[] OptionalColumns =
    [
        "print_channel", "print_mode", "template_version", "logo_asset_id", "logo_brand_name",
        "logo_width_mm", "logo_height_mm", "logo_fit_mode", "logo_position",
        "calibration_profile_id", "trace_code", "client_action_id"
    ];

    public async Task<LabelTraceIssued> RegisterAsync(LabelPrintRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SnapshotJson);
        if (request.TenantId == Guid.Empty || request.UserId == Guid.Empty)
            throw new InvalidOperationException("Tenant e usuário autenticado são obrigatórios para registrar a impressão.");

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.SnapshotJson))).ToLowerInvariant();
        await using var db = await dbFactory.OpenAsync(cancellationToken);
        var printColumns = await Schema.GetColumnsAsync(db, "ged", "label_print", cancellationToken);
        var historyColumns = await Schema.GetColumnsAsync(db, "ged", "label_print_history", cancellationToken);
        LogLegacyColumns("ged.label_print", printColumns);
        LogLegacyColumns("ged.label_print_history", historyColumns);

        // Idempotency: avoid double print on duplicate clientActionId
        if (request.ClientActionId is Guid actionId && printColumns.Contains("client_action_id"))
        {
            var existing = await db.QuerySingleOrDefaultAsync<ExistingPrintRow>(new CommandDefinition("""
select p.id, t.trace_code TraceCode, t.short_url ShortUrl, t.payload_hash PayloadHash
from ged.label_print p
left join ged.label_trace_identity t on t.tenant_id = p.tenant_id and t.label_print_id = p.id
where p.tenant_id = @TenantId and p.client_action_id = @actionId and p.reg_status = 'A'
limit 1
""", new { request.TenantId, actionId }, cancellationToken: cancellationToken));

            if (existing is not null)
            {
                logger.LogInformation("Idempotência aplicada para ClientActionId={ActionId} no tenant {TenantId}.", actionId, request.TenantId);
                return new LabelTraceIssued(
                    new LabelTracePublicInfo(existing.Id, existing.TraceCode ?? "", request.SubjectType, request.TemplateCode, request.TemplateVersion, LabelTraceStatus.Active, DateTime.UtcNow, request.TenantId),
                    "",
                    existing.ShortUrl ?? $"/Labels/Trace/{existing.TraceCode}",
                    existing.Id);
            }
        }

        await using var tx = await db.BeginTransactionAsync(cancellationToken);
        var priorPrints = await db.ExecuteScalarAsync<int>(new CommandDefinition("""
select count(*) from ged.label_print_history
where tenant_id=@TenantId and label_subject_type=@SubjectType and label_subject_id=@SubjectId and template_code=@TemplateCode;
""", request, tx, cancellationToken: cancellationToken));
        if (priorPrints > 0 && string.IsNullOrWhiteSpace(request.ReprintReason))
            throw new InvalidOperationException("O motivo da reimpressão é obrigatório.");

        var boxId = request.SubjectType.Equals("BOX", StringComparison.OrdinalIgnoreCase) ? request.SubjectId : (Guid?)null;
        var documentId = request.SubjectType.Equals("DOCUMENT", StringComparison.OrdinalIgnoreCase) ? request.SubjectId : (Guid?)null;
        var parameters = CreateParameters(request, hash, boxId, documentId);
        var labelPrintId = await db.ExecuteScalarAsync<Guid>(new CommandDefinition(
            BuildPrintInsert(printColumns), parameters, tx, cancellationToken: cancellationToken));
        await db.ExecuteAsync(new CommandDefinition(
            BuildHistoryInsert(historyColumns), parameters, tx, cancellationToken: cancellationToken));
        await tx.CommitAsync(cancellationToken);

        var issued = await traceability.IssueAsync(new(request.TenantId, labelPrintId, request.SubjectType, request.SubjectId,
            request.TemplateCode, null, request.UserId, null, hash), cancellationToken);
        await custody.RegisterEventAsync(new(request.TenantId, request.SubjectType, request.SubjectId, null,
            priorPrints > 0 ? "LABEL_REPRINTED" : "LABEL_PRINTED", priorPrints > 0 ? "Etiqueta reimpressa" : "Etiqueta impressa",
            request.ReprintReason, "label_print_history", null, null, null, request.UserId, request.IpAddress, request.UserAgent, request.SnapshotJson), cancellationToken);

        return new LabelTraceIssued(issued.Trace, issued.Token, issued.ShortUrl, labelPrintId);
    }

    public async Task UpdateFinalSnapshotAsync(Guid tenantId, Guid labelPrintId, string snapshotJson, string? traceCode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotJson);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshotJson))).ToLowerInvariant();
        await using var db = await dbFactory.OpenAsync(cancellationToken);
        await db.ExecuteAsync(new CommandDefinition("""
update ged.label_print
set snapshot_json = cast(@snapshotJson as jsonb),
    data = cast(@snapshotJson as jsonb),
    payload_hash_sha256 = @hash,
    trace_code = coalesce(nullif(@traceCode, ''), trace_code)
where tenant_id = @tenantId and id = @labelPrintId;

update ged.label_print_history
set snapshot_json = cast(@snapshotJson as jsonb),
    snapshot_sha256 = @hash
where tenant_id = @tenantId and id = @labelPrintId;

update ged.label_trace_identity
set payload_hash = @hash
where tenant_id = @tenantId and label_print_id = @labelPrintId;
""", new { tenantId, labelPrintId, snapshotJson, hash, traceCode }, cancellationToken: cancellationToken));
    }

    private void LogLegacyColumns(string table, IReadOnlySet<string> columns)
    {
        var missing = OptionalColumns.Where(column => !columns.Contains(column)).ToArray();
        if (missing.Length > 0)
            logger.LogWarning("Schema legado detectado em {Table}; colunas opcionais omitidas do registro: {Columns}. Aplique as migrations obrigatórias.", table, string.Join(", ", missing));
    }

    private static DynamicParameters CreateParameters(LabelPrintRequest request, string hash, Guid? boxId, Guid? documentId)
    {
        var values = new DynamicParameters(new
        {
            Id = Guid.NewGuid(), request.TenantId, BoxId = boxId, DocumentId = documentId,
            request.SubjectType, request.SubjectId, request.UserId, request.IpAddress, request.UserAgent,
            request.SnapshotJson, Hash = hash, request.TemplateCode, request.ReprintReason,
            request.PrintChannel, request.PrintMode, request.TemplateVersion, request.LogoAssetId,
            request.LogoBrandName, request.LogoWidthMm, request.LogoHeightMm, request.LogoFitMode,
            request.LogoPosition, request.CalibrationProfileId, request.TraceCode, request.ClientActionId
        });
        return values;
    }

    private static string BuildPrintInsert(IReadOnlySet<string> available)
    {
        var columns = new List<string> { "id", "tenant_id", "box_id", "document_id", "label_type", "printed_by", "ip_address", "user_agent", "data", "snapshot_json", "payload_hash_sha256", "reprint_reason", "reg_status" };
        var values = new List<string> { "@Id", "@TenantId", "@BoxId", "@DocumentId", "@SubjectType", "@UserId", "cast(@IpAddress as inet)", "@UserAgent", "cast(@SnapshotJson as jsonb)", "cast(@SnapshotJson as jsonb)", "@Hash", "nullif(@ReprintReason, '')", "'A'" };
        AddOptional(available, columns, values);
        return $"insert into ged.label_print ({string.Join(", ", columns)}) values ({string.Join(", ", values)}) returning id;";
    }

    private static string BuildHistoryInsert(IReadOnlySet<string> available)
    {
        var columns = new List<string> { "id", "tenant_id", "label_subject_type", "label_subject_id", "template_code", "snapshot_json", "snapshot_sha256", "printed_by", "ip_address", "user_agent", "reprint_reason" };
        var values = new List<string> { "@Id", "@TenantId", "@SubjectType", "@SubjectId", "@TemplateCode", "cast(@SnapshotJson as jsonb)", "@Hash", "@UserId", "cast(@IpAddress as inet)", "@UserAgent", "nullif(@ReprintReason, '')" };
        AddOptional(available, columns, values);
        return $"insert into ged.label_print_history ({string.Join(", ", columns)}) values ({string.Join(", ", values)});";
    }

    private static void AddOptional(IReadOnlySet<string> available, ICollection<string> columns, ICollection<string> values)
    {
        var optionalValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["print_channel"] = "@PrintChannel", ["print_mode"] = "@PrintMode", ["template_version"] = "@TemplateVersion",
            ["logo_asset_id"] = "@LogoAssetId", ["logo_brand_name"] = "@LogoBrandName", ["logo_width_mm"] = "@LogoWidthMm",
            ["logo_height_mm"] = "@LogoHeightMm", ["logo_fit_mode"] = "@LogoFitMode", ["logo_position"] = "@LogoPosition",
            ["calibration_profile_id"] = "@CalibrationProfileId", ["trace_code"] = "@TraceCode",
            ["client_action_id"] = "@ClientActionId"
        };
        foreach (var (column, value) in optionalValues.Where(item => available.Contains(item.Key)))
        {
            columns.Add(column);
            values.Add(value);
        }
    }

    private sealed class ExistingPrintRow
    {
        public Guid Id { get; init; }
        public string? TraceCode { get; init; }
        public string? ShortUrl { get; init; }
        public string? PayloadHash { get; init; }
    }
}
