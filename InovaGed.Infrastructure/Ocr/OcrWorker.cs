using System.Text.Json;
using Dapper;
using InovaGed.Application;
using InovaGed.Application.Classification;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Common.Storage;
using InovaGed.Application.Documents;
using InovaGed.Application.Ocr;
using InovaGed.Infrastructure.Preview;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ocr;

internal static class SystemUsers
{
    public static readonly Guid OcrWorker = Guid.Parse("00000000-0000-0000-0000-000000000999");
}

public sealed class OcrWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OcrWorker> _logger;

    public OcrWorker(IServiceScopeFactory scopeFactory, ILogger<OcrWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OCR Worker iniciado.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();

                var jobs = scope.ServiceProvider.GetRequiredService<IOcrJobRepository>();
                var docs = scope.ServiceProvider.GetRequiredService<IDocumentQueries>();
                var preview = scope.ServiceProvider.GetRequiredService<IPreviewGenerator>();
                var ocr = scope.ServiceProvider.GetRequiredService<IOcrService>();
                var app = scope.ServiceProvider.GetRequiredService<DocumentAppService>();
                var db = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();

                var job = await jobs.DequeueAndMarkProcessingAsync(stoppingToken);

                if (job is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }

                var actorId = job.RequestedBy ?? SystemUsers.OcrWorker;

                _logger.LogInformation(
                    "Processando OCR JobId={JobId}, VersionId={VersionId}, Actor={ActorId}",
                    job.Id,
                    job.DocumentVersionId,
                    actorId);

                await jobs.RenewLeaseAsync(job.Id, stoppingToken);

                var v = await docs.GetVersionForDownloadAsync(job.TenantId, job.DocumentVersionId, stoppingToken);

                if (v is null)
                {
                    await jobs.MarkErrorAsync(job.Id, "Versão não encontrada.", stoppingToken);
                    continue;
                }

                await InsertDocumentAuditAsync(
                    db,
                    job.TenantId,
                    v.DocumentId,
                    actorId,
                    "OCR_STARTED",
                    "OCR",
                    null,
                    new
                    {
                        jobId = job.Id,
                        sourceVersionId = job.DocumentVersionId,
                        fileName = v.FileName
                    },
                    "OCR_WORKER",
                    stoppingToken);

                await jobs.RenewLeaseAsync(job.Id, stoppingToken);

                var latestStatus = await jobs.GetLatestByVersionIdAsync(job.TenantId, job.DocumentVersionId, stoppingToken);

                if (latestStatus is not null &&
                    latestStatus.Status.ToString().Equals("COMPLETED", StringComparison.OrdinalIgnoreCase) &&
                    latestStatus.JobId != job.Id)
                {
                    await jobs.MarkErrorAsync(job.Id, "OCR não executado: já existe OCR concluído para esta versão.", stoppingToken);

                    await InsertDocumentAuditAsync(
                        db,
                        job.TenantId,
                        v.DocumentId,
                        actorId,
                        "OCR_DUPLICATED_IGNORED",
                        "OCR",
                        null,
                        new
                        {
                            jobId = job.Id,
                            sourceVersionId = job.DocumentVersionId
                        },
                        "OCR_WORKER",
                        stoppingToken);

                    continue;
                }

                string pdfPath;

                if (IsPdf(v.ContentType, v.FileName))
                {
                    pdfPath = v.StoragePath;
                }
                else
                {
                    pdfPath = await preview.GetOrCreatePreviewPdfAsync(
                        job.TenantId,
                        v.DocumentId,
                        v.VersionId,
                        v.StoragePath,
                        v.FileName,
                        stoppingToken);
                }

                await jobs.RenewLeaseAsync(job.Id, stoppingToken);

                try
                {
                    var result = await ocr.OcrizePdfAsync(
                        pdfStoragePath: pdfPath,
                        invalidateDigitalSignatures: job.InvalidateDigitalSignatures,
                        ct: stoppingToken);

                    if (result?.OcrPdfBytes is null || result.OcrPdfBytes.Length == 0)
                    {
                        await jobs.MarkErrorAsync(job.Id, "OCR não retornou PDF válido.", stoppingToken);

                        await InsertDocumentAuditAsync(
                            db,
                            job.TenantId,
                            v.DocumentId,
                            actorId,
                            "OCR_ERROR",
                            "OCR",
                            null,
                            new
                            {
                                jobId = job.Id,
                                error = "OCR não retornou PDF válido."
                            },
                            "OCR_WORKER",
                            stoppingToken);

                        continue;
                    }

                    var extractedText = result.ExtractedText ?? "";

                    var newFileName = BuildOcrFileName(v.FileName);

                    await using var ms = new MemoryStream(result.OcrPdfBytes);
                    ms.Position = 0;

                    await jobs.RenewLeaseAsync(job.Id, stoppingToken);

                    var add = await app.AddVersionWithOcrAsyncBackground(
                        tenantId: job.TenantId,
                        actorId: actorId,
                        documentId: v.DocumentId,
                        content: ms,
                        fileName: newFileName,
                        contentType: "application/pdf",
                        ocrText: extractedText,
                        ip: "worker",
                        userAgent: "OcrWorker",
                        ct: stoppingToken);

                    if (!add.Success)
                    {
                        var error = add.Error?.Message ?? "Falha ao salvar versão OCR.";

                        await jobs.MarkErrorAsync(job.Id, error, stoppingToken);

                        await InsertDocumentAuditAsync(
                            db,
                            job.TenantId,
                            v.DocumentId,
                            actorId,
                            "OCR_ERROR",
                            "OCR",
                            null,
                            new
                            {
                                jobId = job.Id,
                                error
                            },
                            "OCR_WORKER",
                            stoppingToken);

                        continue;
                    }

                    await UpdateDocumentDescriptionFromOcrAsync(
                        db,
                        job.TenantId,
                        v.DocumentId,
                        actorId,
                        extractedText,
                        stoppingToken);

                    await RunClassificationAsync(
                        scope,
                        job,
                        v.DocumentId,
                        add.Value,
                        actorId,
                        extractedText,
                        stoppingToken);

                    await jobs.MarkCompletedAsync(job.Id, v.DocumentId, add.Value, stoppingToken);

                    await InsertDocumentAuditAsync(
                        db,
                        job.TenantId,
                        v.DocumentId,
                        actorId,
                        "OCR_COMPLETED",
                        "OCR",
                        null,
                        new
                        {
                            jobId = job.Id,
                            sourceVersionId = job.DocumentVersionId,
                            resultVersionId = add.Value,
                            descriptionUpdated = !string.IsNullOrWhiteSpace(extractedText)
                        },
                        "OCR_WORKER",
                        stoppingToken);

                    _logger.LogInformation(
                        "OCR concluído. JobId={JobId}, DocumentId={DocumentId}, NewVersion={NewVersionId}",
                        job.Id,
                        v.DocumentId,
                        add.Value);
                }
                catch (PdfHasDigitalSignatureException)
                {
                    const string message = "Documento possui assinatura digital. Reexecute com FORÇAR OCR.";

                    await jobs.MarkErrorAsync(job.Id, message, stoppingToken);

                    await InsertDocumentAuditAsync(
                        db,
                        job.TenantId,
                        v.DocumentId,
                        actorId,
                        "OCR_ERROR",
                        "OCR",
                        null,
                        new
                        {
                            jobId = job.Id,
                            error = message
                        },
                        "OCR_WORKER",
                        stoppingToken);
                }
                catch (Exception ex)
                {
                    await jobs.MarkErrorAsync(job.Id, ex.Message, stoppingToken);

                    await InsertDocumentAuditAsync(
                        db,
                        job.TenantId,
                        v.DocumentId,
                        actorId,
                        "OCR_ERROR",
                        "OCR",
                        null,
                        new
                        {
                            jobId = job.Id,
                            error = ex.Message
                        },
                        "OCR_WORKER",
                        stoppingToken);

                    _logger.LogError(ex, "Erro ao processar OCR. JobId={JobId}", job.Id);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(300), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha inesperada no OCR Worker.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        _logger.LogInformation("OCR Worker finalizado.");
    }

    private static async Task RunClassificationAsync(
        IServiceScope scope,
        OcrJobDto job,
        Guid documentId,
        Guid newVersionId,
        Guid actorId,
        string extractedText,
        CancellationToken ct)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<OcrWorker>>();

        try
        {
            var classifier = scope.ServiceProvider.GetRequiredService<IDocumentClassifier>();
            var classRepo = scope.ServiceProvider.GetRequiredService<IDocumentClassificationRepository>();
            var classCommands = scope.ServiceProvider.GetRequiredService<IDocumentClassificationCommands>();

            var classified = await classifier.ClassifyAsync(
                tenantId: job.TenantId,
                documentId: documentId,
                documentVersionId: newVersionId,
                ocrText: extractedText,
                ct: ct);

            await classRepo.UpsertClassificationAsync(
                tenantId: job.TenantId,
                documentId: documentId,
                documentVersionId: newVersionId,
                documentTypeId: classified.DocumentTypeId,
                confidence: classified.Confidence,
                method: classified.Method,
                summary: classified.Summary,
                classifiedBy: actorId,
                ct: ct);

            await classRepo.UpsertTagsAsync(
                tenantId: job.TenantId,
                documentId: documentId,
                tags: classified.Tags,
                method: classified.Method,
                assignedBy: actorId,
                ct: ct);

            await classRepo.UpsertMetadataAsync(
                tenantId: job.TenantId,
                documentId: documentId,
                metadata: classified.Metadata,
                method: classified.Method,
                ct: ct);

            if (classified.DocumentTypeId.HasValue && classified.DocumentTypeId.Value != Guid.Empty)
            {
                await classCommands.SaveSuggestionOnlyAsync(
                    tenantId: job.TenantId,
                    documentId: documentId,
                    suggestedTypeId: classified.DocumentTypeId.Value,
                    suggestedConfidence: classified.Confidence,
                    suggestedSummary: classified.Summary,
                    ct: ct);
            }
            else
            {
                logger.LogWarning(
                    "OCR executado, mas nenhuma sugestão de tipo documental foi gerada. Doc={DocId}, Version={VersionId}",
                    documentId,
                    newVersionId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Falha na classificação automática OCR. O OCR foi concluído, mas a sugestão não foi gravada. Doc={DocId}",
                documentId);
        }
    }

    private static async Task UpdateDocumentDescriptionFromOcrAsync(
        IDbConnectionFactory db,
        Guid tenantId,
        Guid documentId,
        Guid actorId,
        string extractedText,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(extractedText))
            return;

        var description = BuildDescription(extractedText);

        await using var conn = await db.OpenAsync(ct);

        const string beforeSql = @"
SELECT jsonb_build_object(
    'documentId', id,
    'description', description
)::text
FROM ged.document
WHERE tenant_id = @tenantId
  AND id = @documentId;";

        var beforeJson = await conn.ExecuteScalarAsync<string?>(
            new CommandDefinition(
                beforeSql,
                new { tenantId, documentId },
                cancellationToken: ct)) ?? "{}";

        const string updateSql = @"
UPDATE ged.document
SET description = @description,
    updated_at = now(),
    updated_by = @actorId
WHERE tenant_id = @tenantId
  AND id = @documentId
  AND (
        description IS NULL
        OR btrim(description) = ''
        OR btrim(description) = '-'
      );";

        var rows = await conn.ExecuteAsync(
            new CommandDefinition(
                updateSql,
                new { tenantId, documentId, actorId, description },
                cancellationToken: ct));

        if (rows > 0)
        {
            await InsertDocumentAuditAsync(
                db,
                tenantId,
                documentId,
                actorId,
                "DOCUMENT_DESCRIPTION_UPDATED_FROM_OCR",
                "OCR",
                beforeJson,
                new
                {
                    documentId,
                    description
                },
                "OCR_WORKER",
                ct);
        }
    }

    private static string BuildDescription(string text)
    {
        var clean = string.Join(
            " ",
            text.Replace("\r", " ")
                .Replace("\n", " ")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (clean.Length <= 500)
            return clean;

        return clean[..500] + "...";
    }

    private static async Task InsertDocumentAuditAsync(
        IDbConnectionFactory db,
        Guid tenantId,
        Guid documentId,
        Guid? userId,
        string action,
        string method,
        object? before,
        object? after,
        string source,
        CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);

        var beforeJson = before is null
            ? "{}"
            : before is string s
                ? s
                : JsonSerializer.Serialize(before);

        var afterJson = after is null
            ? "{}"
            : JsonSerializer.Serialize(after);

        const string sql = @"
INSERT INTO ged.document_classification_audit
(
  id, tenant_id, document_id, user_id,
  action, method,
  before_json, after_json,
  source, created_at, reg_status
)
VALUES
(
  gen_random_uuid(), @tenantId, @documentId, @userId,
  @action, @method,
  @beforeJson::jsonb, @afterJson::jsonb,
  @source, now(), 'A'
);";

        await conn.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    tenantId,
                    documentId,
                    userId,
                    action,
                    method,
                    beforeJson,
                    afterJson,
                    source
                },
                cancellationToken: ct));
    }

    private static string BuildOcrFileName(string originalFileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(originalFileName);

        if (baseName.EndsWith("_OCR", StringComparison.OrdinalIgnoreCase))
            return $"{baseName}.pdf";

        return $"{baseName}_OCR.pdf";
    }

    private static bool IsPdf(string? ct, string name)
        => (!string.IsNullOrWhiteSpace(ct) && ct.Contains("pdf", StringComparison.OrdinalIgnoreCase))
           || name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
}