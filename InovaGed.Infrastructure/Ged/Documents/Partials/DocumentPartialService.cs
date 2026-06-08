using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Documents.Partials;
using InovaGed.Application.Ocr;
using InovaGed.Domain.Primitives;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ged.Documents.Partials;

public sealed class DocumentPartialService : IDocumentPartialService
{
    private readonly IDbConnectionFactory _db;
    private readonly IAuditWriter _audit;
    private readonly IOcrJobRepository _ocrJobs;
    private readonly ILogger<DocumentPartialService> _logger;

    public DocumentPartialService(IDbConnectionFactory db, IAuditWriter audit, IOcrJobRepository ocrJobs, ILogger<DocumentPartialService> logger)
    {
        _db = db;
        _audit = audit;
        _ocrJobs = ocrJobs;
        _logger = logger;
    }


    public async Task<Result<DocumentPartialSummaryDto>> MarkAsIncompleteAsync(Guid tenantId, Guid userId, Guid documentId, int? totalParts, string? notes, string? correlationId, CancellationToken ct)
    {
        if (tenantId == Guid.Empty || documentId == Guid.Empty)
            return Result<DocumentPartialSummaryDto>.Fail("VALIDATION", "Documento inválido.");
        if (totalParts.HasValue && totalParts.Value <= 0)
            return Result<DocumentPartialSummaryDto>.Fail("VALIDATION", "Total previsto de partes deve ser maior que zero.");

        await using var conn = await _db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            var row = await conn.QuerySingleOrDefaultAsync(new CommandDefinition("""
WITH document_row AS (
    SELECT d.id, d.current_version_id
    FROM ged.document d
    WHERE d.tenant_id=@tenantId
      AND d.id=@documentId
      AND COALESCE(d.reg_status, 'A')='A'
    LIMIT 1
), current_version AS (
    SELECT v.id, v.partial_group_id, v.partial_part_number, v.part_number, v.partial_total_parts, v.total_parts, v.file_name, v.file_size_bytes
    FROM document_row d
    JOIN ged.document_version v ON v.tenant_id=@tenantId AND v.id=d.current_version_id AND v.document_id=d.id
    LIMIT 1
), fallback_version AS (
    SELECT v.id, v.partial_group_id, v.partial_part_number, v.part_number, v.partial_total_parts, v.total_parts, v.file_name, v.file_size_bytes
    FROM document_row d
    JOIN ged.document_version v ON v.tenant_id=@tenantId AND v.document_id=d.id
    WHERE NOT EXISTS (SELECT 1 FROM current_version)
    ORDER BY COALESCE(v.uploaded_at_utc, v.created_at, '-infinity'::timestamptz) DESC, v.id DESC
    LIMIT 1
), selected_version AS (
    SELECT * FROM current_version
    UNION ALL
    SELECT * FROM fallback_version
), updated_version AS (
    UPDATE ged.document_version v
    SET is_partial_document=true,
        is_document_incomplete=true,
        partial_group_id=COALESCE(v.partial_group_id, @newGroupId),
        partial_part_number=COALESCE(v.partial_part_number, v.part_number, 1),
        partial_total_parts=COALESCE(@totalParts, v.partial_total_parts, v.total_parts),
        partial_status='INCOMPLETE',
        part_number=COALESCE(v.part_number, v.partial_part_number, 1),
        total_parts=COALESCE(@totalParts, v.total_parts, v.partial_total_parts),
        uploaded_at_utc=COALESCE(v.uploaded_at_utc, v.created_at, now())
    FROM selected_version sv
    WHERE v.tenant_id=@tenantId AND v.document_id=@documentId AND v.id=sv.id
    RETURNING v.id, v.partial_group_id, COALESCE(v.partial_part_number, v.part_number, 1) AS part_number, v.partial_total_parts, v.file_name, v.file_size_bytes
), insert_part AS (
    INSERT INTO ged.document_partial_part
        (tenant_id, document_id, version_id, partial_group_id, part_number, total_parts, file_name, size_bytes, uploaded_at_utc, uploaded_by, status, notes)
    SELECT @tenantId, @documentId, id, partial_group_id, part_number, partial_total_parts, file_name, file_size_bytes, now(), @userId, 'UPLOADED', @notes
    FROM updated_version uv
    ON CONFLICT (tenant_id, version_id) DO UPDATE
    SET partial_group_id=EXCLUDED.partial_group_id,
        part_number=EXCLUDED.part_number,
        total_parts=EXCLUDED.total_parts,
        file_name=EXCLUDED.file_name,
        size_bytes=EXCLUDED.size_bytes,
        uploaded_at_utc=EXCLUDED.uploaded_at_utc,
        uploaded_by=EXCLUDED.uploaded_by,
        status='UPLOADED',
        notes=EXCLUDED.notes,
        reg_status='A'
    RETURNING version_id AS id, partial_group_id, part_number, total_parts AS partial_total_parts
)
SELECT id, partial_group_id, part_number, partial_total_parts FROM insert_part
LIMIT 1;
""", new { tenantId, documentId, totalParts, newGroupId = Guid.NewGuid(), userId, notes }, tx, cancellationToken: ct));
            if (row is null)
            {
                await tx.RollbackAsync(ct);
                return Result<DocumentPartialSummaryDto>.Fail("NOT_FOUND", "Documento não possui versão atual para marcar como incompleto.");
            }

            Guid groupId = row.partial_group_id;
            var summary = await BuildSummaryAsync(conn, tx, tenantId, documentId, groupId, ct);
            await tx.CommitAsync(ct);

            await WriteAuditAsync(tenantId, userId, "DOCUMENT_MARK_INCOMPLETE", documentId, new { documentId, versionId = (Guid)row.id, partialGroupId = groupId, partNumber = (int)row.part_number, totalParts, userId, tenantId, correlationId, notes, timestampUtc = DateTime.UtcNow }, ct);
            return Result<DocumentPartialSummaryDto>.Ok(summary with { PartialStatus = "INCOMPLETE", CanConsolidate = false });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(CancellationToken.None);
            _logger.LogError(ex, "Erro ao marcar documento como incompleto. Tenant={TenantId} Document={DocumentId}", tenantId, documentId);
            return Result<DocumentPartialSummaryDto>.Fail("ERR", "Não foi possível marcar o documento como incompleto. Verifique se o banco está atualizado.");
        }
    }

    public async Task<Result<DocumentPartialSummaryDto>> AddPartAsync(AddDocumentPartRequest request, CancellationToken ct)
    {
        if (request.TenantId == Guid.Empty || request.DocumentId == Guid.Empty || request.VersionId == Guid.Empty)
            return Result<DocumentPartialSummaryDto>.Fail("VALIDATION", "Documento ou versão inválidos.");
        if (request.PartNumber <= 0)
            return Result<DocumentPartialSummaryDto>.Fail("VALIDATION", "Número da parte é obrigatório.");
        if (request.TotalParts.HasValue && request.TotalParts.Value < request.PartNumber)
            return Result<DocumentPartialSummaryDto>.Fail("VALIDATION", "Total de partes deve ser maior ou igual ao número da parte.");

        await using var conn = await _db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            var groupId = request.PartialGroupId ?? await ResolvePartialGroupIdAsync(conn, tx, request.TenantId, request.DocumentId, ct) ?? Guid.NewGuid();
            var duplicate = await conn.ExecuteScalarAsync<int>(new CommandDefinition("""
SELECT count(*)::int
FROM ged.document_partial_part
WHERE tenant_id=@TenantId
  AND partial_group_id=@GroupId
  AND part_number=@PartNumber
  AND version_id <> @VersionId
  AND reg_status='A';
""", new { request.TenantId, GroupId = groupId, request.PartNumber, request.VersionId }, tx, cancellationToken: ct));
            if (duplicate > 0)
            {
                await tx.RollbackAsync(ct);
                return Result<DocumentPartialSummaryDto>.Fail("DUPLICATE_PART", "Já existe uma parte com este número para o documento fracionado.");
            }

            await conn.ExecuteAsync(new CommandDefinition("""
UPDATE ged.document_version
SET is_partial_document=true,
    is_document_incomplete=true,
    partial_group_id=@GroupId,
    partial_part_number=@PartNumber,
    partial_total_parts=@TotalParts,
    partial_status='INCOMPLETE',
    part_number=@PartNumber,
    total_parts=@TotalParts,
    uploaded_at_utc=COALESCE(@UploadedAtUtc, uploaded_at_utc, created_at, now())
WHERE tenant_id=@TenantId AND document_id=@DocumentId AND id=@VersionId;

INSERT INTO ged.document_partial_part
    (tenant_id, document_id, version_id, partial_group_id, part_number, total_parts, file_name, size_bytes, uploaded_at_utc, uploaded_by, status, notes)
VALUES
    (@TenantId, @DocumentId, @VersionId, @GroupId, @PartNumber, @TotalParts, @FileName, @SizeBytes, COALESCE(@UploadedAtUtc, now()), @UserId, 'UPLOADED', @Notes)
ON CONFLICT (tenant_id, version_id) DO UPDATE
SET partial_group_id=EXCLUDED.partial_group_id,
    part_number=EXCLUDED.part_number,
    total_parts=EXCLUDED.total_parts,
    file_name=EXCLUDED.file_name,
    size_bytes=EXCLUDED.size_bytes,
    uploaded_at_utc=EXCLUDED.uploaded_at_utc,
    uploaded_by=EXCLUDED.uploaded_by,
    status='UPLOADED',
    notes=EXCLUDED.notes,
    reg_status='A';
""", new
            {
                request.TenantId,
                request.DocumentId,
                request.VersionId,
                GroupId = groupId,
                request.PartNumber,
                request.TotalParts,
                request.FileName,
                request.SizeBytes,
                UploadedAtUtc = request.UploadedAtUtc == default ? DateTime.UtcNow : request.UploadedAtUtc,
                request.UserId,
                request.Notes
            }, tx, cancellationToken: ct));

            var summary = await RefreshStatusAsync(conn, tx, request.TenantId, request.DocumentId, groupId, ct);
            await tx.CommitAsync(ct);

            await WriteAuditAsync(request.TenantId, request.UserId, "DOCUMENT_PART_ADD", request.DocumentId, new { documentId = request.DocumentId, versionId = request.VersionId, partialGroupId = groupId, partNumber = request.PartNumber, totalParts = request.TotalParts, userId = request.UserId, tenantId = request.TenantId, correlationId = request.CorrelationId, timestampUtc = DateTime.UtcNow }, ct);
            return Result<DocumentPartialSummaryDto>.Ok(summary);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(CancellationToken.None);
            _logger.LogError(ex, "Erro ao registrar parte de documento. Tenant={TenantId} Document={DocumentId} Version={VersionId}", request.TenantId, request.DocumentId, request.VersionId);
            return Result<DocumentPartialSummaryDto>.Fail("ERR", "Não foi possível registrar a parte do documento.");
        }
    }

    public async Task<IReadOnlyList<DocumentPartialPartDto>> GetPartsAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        const string sql = """
SELECT pp.id AS "Id", pp.tenant_id AS "TenantId", pp.document_id AS "DocumentId", pp.version_id AS "VersionId",
       pp.partial_group_id AS "PartialGroupId", pp.part_number AS "PartNumber", pp.total_parts AS "TotalParts",
       pp.file_name AS "FileName", pp.size_bytes AS "SizeBytes", pp.uploaded_at_utc AS "UploadedAtUtc",
       pp.uploaded_by AS "UploadedBy", COALESCE(u.user_name, pp.uploaded_by::text) AS "UploadedByName",
       pp.status AS "Status", pp.notes AS "Notes",
       oj.status::text AS "OcrStatus",
       (NULLIF(COALESCE(ds.ocr_text,''),'') IS NOT NULL) AS "HasOcrText",
       (upper(COALESCE(oj.status::text,'')) = 'COMPLETED' AND NULLIF(COALESCE(ds.ocr_text,''),'') IS NOT NULL) AS "IsOcrAvailable"
FROM ged.document_partial_part pp
LEFT JOIN ged.app_user u ON u.tenant_id=pp.tenant_id AND u.id=pp.uploaded_by
LEFT JOIN LATERAL (
    SELECT j.* FROM ged.ocr_job j
    WHERE j.tenant_id=pp.tenant_id AND j.document_version_id=pp.version_id
    ORDER BY j.requested_at DESC LIMIT 1
) oj ON true
LEFT JOIN ged.document_search ds ON ds.tenant_id=pp.tenant_id AND ds.document_id=pp.document_id AND ds.version_id=pp.version_id
WHERE pp.tenant_id=@tenantId AND pp.document_id=@documentId AND pp.reg_status='A'
ORDER BY pp.part_number, pp.uploaded_at_utc;
""";
        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<DocumentPartialPartDto>(new CommandDefinition(sql, new { tenantId, documentId }, cancellationToken: ct));
        await WriteAuditAsync(tenantId, null, "DOCUMENT_PART_VIEW", documentId, new { documentId, tenantId, correlationId = (string?)null, timestampUtc = DateTime.UtcNow }, ct);
        return rows.AsList();
    }

    public async Task<Result<DocumentPartialSummaryDto>> MarkAsCompleteAsync(Guid tenantId, Guid userId, Guid documentId, string? correlationId, CancellationToken ct)
        => await ChangeStatusAsync(tenantId, userId, documentId, "COMPLETE", "DOCUMENT_PART_MARK_COMPLETE", null, correlationId, ct);

    public async Task<Result<DocumentPartialSummaryDto>> CancelPartialAsync(Guid tenantId, Guid userId, Guid documentId, string? reason, string? correlationId, CancellationToken ct)
        => await ChangeStatusAsync(tenantId, userId, documentId, "CANCELLED", "DOCUMENT_PART_CANCEL", reason, correlationId, ct);

    public async Task<Result<DocumentPartialSummaryDto>> ConsolidateAsync(Guid tenantId, Guid userId, Guid documentId, string? correlationId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            var groupId = await ResolvePartialGroupIdAsync(conn, tx, tenantId, documentId, ct);
            if (!groupId.HasValue)
                return Result<DocumentPartialSummaryDto>.Fail("NOT_PARTIAL", "Documento não possui partes registradas.");

            var summary = await BuildSummaryAsync(conn, tx, tenantId, documentId, groupId.Value, ct);
            if (!summary.CanConsolidate)
                return Result<DocumentPartialSummaryDto>.Fail("NOT_READY", "Documento ainda não possui partes suficientes para consolidar.");

            var consolidatedVersionId = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
SELECT version_id
FROM ged.document_partial_part
WHERE tenant_id=@tenantId AND partial_group_id=@groupId AND reg_status='A'
ORDER BY part_number DESC, uploaded_at_utc DESC
LIMIT 1;
""", new { tenantId, groupId }, tx, cancellationToken: ct));
            if (!consolidatedVersionId.HasValue)
                return Result<DocumentPartialSummaryDto>.Fail("NOT_READY", "Nenhuma versão de parte encontrada para consolidação.");

            await conn.ExecuteAsync(new CommandDefinition("""
UPDATE ged.document_version
SET partial_status='CONSOLIDATED',
    is_document_incomplete=false,
    consolidated_version_id=@consolidatedVersionId
WHERE tenant_id=@tenantId AND partial_group_id=@groupId;

UPDATE ged.document_partial_part
SET status='CONSOLIDATED'
WHERE tenant_id=@tenantId AND partial_group_id=@groupId AND reg_status='A';

UPDATE ged.document
SET current_version_id=@consolidatedVersionId
WHERE tenant_id=@tenantId AND id=@documentId;
""", new { tenantId, documentId, groupId, consolidatedVersionId }, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);

            await _ocrJobs.EnqueueAsync(tenantId, consolidatedVersionId.Value, userId, invalidateDigitalSignatures: false, ct);
            await WriteAuditAsync(tenantId, userId, "DOCUMENT_PART_CONSOLIDATE", documentId, new { documentId, versionId = consolidatedVersionId, partialGroupId = groupId, userId, tenantId, correlationId, timestampUtc = DateTime.UtcNow, consolidationMode = "LOGICAL", technicalTodo = "Implementar mesclagem física de PDFs quando a biblioteca de merge for homologada." }, ct);

            return Result<DocumentPartialSummaryDto>.Ok(summary with { PartialStatus = "CONSOLIDATED", ConsolidatedVersionId = consolidatedVersionId, CanConsolidate = false });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(CancellationToken.None);
            _logger.LogError(ex, "Erro ao consolidar documento fracionado. Tenant={TenantId} Document={DocumentId}", tenantId, documentId);
            return Result<DocumentPartialSummaryDto>.Fail("ERR", "Não foi possível consolidar o documento.");
        }
    }

    private async Task<Result<DocumentPartialSummaryDto>> ChangeStatusAsync(Guid tenantId, Guid userId, Guid documentId, string status, string auditAction, string? reason, string? correlationId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            var groupId = await ResolvePartialGroupIdAsync(conn, tx, tenantId, documentId, ct);
            if (!groupId.HasValue) return Result<DocumentPartialSummaryDto>.Fail("NOT_PARTIAL", "Documento não possui partes registradas.");
            await conn.ExecuteAsync(new CommandDefinition("""
UPDATE ged.document_version
SET partial_status=@status,
    is_document_incomplete=CASE WHEN @status='INCOMPLETE' THEN true ELSE false END
WHERE tenant_id=@tenantId AND partial_group_id=@groupId;
UPDATE ged.document_partial_part
SET status=@partStatus
WHERE tenant_id=@tenantId AND partial_group_id=@groupId AND reg_status='A';
""", new { tenantId, groupId, status, partStatus = status == "CANCELLED" ? "CANCELLED" : "UPLOADED" }, tx, cancellationToken: ct));
            var summary = await BuildSummaryAsync(conn, tx, tenantId, documentId, groupId.Value, ct);
            await tx.CommitAsync(ct);
            await WriteAuditAsync(tenantId, userId, auditAction, documentId, new { documentId, partialGroupId = groupId, userId, tenantId, reason, correlationId, timestampUtc = DateTime.UtcNow }, ct);
            return Result<DocumentPartialSummaryDto>.Ok(summary with { PartialStatus = status, CanConsolidate = status == "COMPLETE" && summary.CanConsolidate });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(CancellationToken.None);
            _logger.LogError(ex, "Erro ao alterar status parcial. Tenant={TenantId} Document={DocumentId} Status={Status}", tenantId, documentId, status);
            return Result<DocumentPartialSummaryDto>.Fail("ERR", "Não foi possível atualizar o status do documento fracionado.");
        }
    }

    private static async Task<Guid?> ResolvePartialGroupIdAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction tx, Guid tenantId, Guid documentId, CancellationToken ct)
        => await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
SELECT partial_group_id
FROM ged.document_version
WHERE tenant_id=@tenantId AND document_id=@documentId AND partial_group_id IS NOT NULL
ORDER BY uploaded_at_utc NULLS LAST, created_at NULLS LAST
LIMIT 1;
""", new { tenantId, documentId }, tx, cancellationToken: ct));

    private static async Task<DocumentPartialSummaryDto> RefreshStatusAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction tx, Guid tenantId, Guid documentId, Guid groupId, CancellationToken ct)
    {
        var summary = await BuildSummaryAsync(conn, tx, tenantId, documentId, groupId, ct);
        var status = summary.TotalParts.HasValue && summary.PartsCount >= summary.TotalParts.Value ? "COMPLETE" : "INCOMPLETE";
        await conn.ExecuteAsync(new CommandDefinition("""
UPDATE ged.document_version
SET partial_status=@status,
    is_document_incomplete=(@status='INCOMPLETE'),
    partial_total_parts=COALESCE(partial_total_parts, @totalParts),
    total_parts=COALESCE(total_parts, @totalParts)
WHERE tenant_id=@tenantId AND partial_group_id=@groupId;
""", new { tenantId, groupId, status, totalParts = summary.TotalParts }, tx, cancellationToken: ct));
        return summary with { PartialStatus = status, CanConsolidate = status == "COMPLETE" && summary.PartsCount > 1 };
    }

    private static async Task<DocumentPartialSummaryDto> BuildSummaryAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction tx, Guid tenantId, Guid documentId, Guid groupId, CancellationToken ct)
    {
        const string sql = """
SELECT @documentId AS "DocumentId",
       @groupId AS "PartialGroupId",
       COALESCE(max(v.partial_status), 'INCOMPLETE') AS "PartialStatus",
       count(pp.id)::int AS "PartsCount",
       max(COALESCE(pp.total_parts, v.partial_total_parts, v.total_parts)) AS "TotalParts",
       (array_agg(v.consolidated_version_id) FILTER (WHERE v.consolidated_version_id IS NOT NULL))[1] AS "ConsolidatedVersionId"
FROM ged.document_partial_part pp
LEFT JOIN ged.document_version v ON v.tenant_id=pp.tenant_id AND v.id=pp.version_id
WHERE pp.tenant_id=@tenantId AND pp.document_id=@documentId AND pp.partial_group_id=@groupId AND pp.reg_status='A';
""";
        var summary = await conn.QuerySingleAsync<DocumentPartialSummaryDto>(new CommandDefinition(sql, new { tenantId, documentId, groupId }, tx, cancellationToken: ct));
        var canConsolidate = summary.PartsCount > 1 && (!summary.TotalParts.HasValue || summary.PartsCount >= summary.TotalParts.Value);
        return summary with { CanConsolidate = canConsolidate };
    }

    private async Task WriteAuditAsync(Guid tenantId, Guid? userId, string action, Guid documentId, object data, CancellationToken ct)
    {
        try { await _audit.WriteAsync(tenantId, userId, action, "DOCUMENT_PART", documentId, action, null, null, data, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Falha ao auditar documento fracionado. Action={Action} Document={DocumentId}", action, documentId); }
    }
}
