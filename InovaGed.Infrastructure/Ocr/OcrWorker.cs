using System.Text.Json;
using Dapper;
using InovaGed.Application;
using InovaGed.Application.Classification;
using InovaGed.Application.Common.Database;
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

                var sourceVersion = await docs.GetVersionForDownloadAsync(
                    job.TenantId,
                    job.DocumentVersionId,
                    stoppingToken);

                if (sourceVersion is null)
                {
                    await jobs.MarkErrorAsync(job.Id, "Versão não encontrada.", stoppingToken);
                    continue;
                }

                await InsertDocumentAuditAsync(
                    db,
                    job.TenantId,
                    sourceVersion.DocumentId,
                    actorId,
                    "OCR_STARTED",
                    "OCR",
                    null,
                    new
                    {
                        jobId = job.Id,
                        sourceVersionId = job.DocumentVersionId,
                        fileName = sourceVersion.FileName
                    },
                    "OCR_WORKER",
                    stoppingToken);

                await jobs.RenewLeaseAsync(job.Id, stoppingToken);

                var latestStatus = await jobs.GetLatestByVersionIdAsync(
                    job.TenantId,
                    job.DocumentVersionId,
                    stoppingToken);

                if (latestStatus is not null &&
                    latestStatus.Status.ToString().Equals("COMPLETED", StringComparison.OrdinalIgnoreCase) &&
                    latestStatus.JobId != job.Id)
                {
                    const string duplicatedMessage = "OCR não executado: já existe OCR concluído para esta versão.";

                    await jobs.MarkErrorAsync(job.Id, duplicatedMessage, stoppingToken);

                    await InsertDocumentAuditAsync(
                        db,
                        job.TenantId,
                        sourceVersion.DocumentId,
                        actorId,
                        "OCR_DUPLICATED_IGNORED",
                        "OCR",
                        null,
                        new
                        {
                            jobId = job.Id,
                            sourceVersionId = job.DocumentVersionId,
                            reason = duplicatedMessage
                        },
                        "OCR_WORKER",
                        stoppingToken);

                    _logger.LogWarning(
                        "OCR duplicado ignorado. JobId={JobId}, VersionId={VersionId}",
                        job.Id,
                        job.DocumentVersionId);

                    continue;
                }

                string pdfPath;

                if (IsPdf(sourceVersion.ContentType, sourceVersion.FileName))
                {
                    pdfPath = sourceVersion.StoragePath;
                }
                else
                {
                    pdfPath = await preview.GetOrCreatePreviewPdfAsync(
                        job.TenantId,
                        sourceVersion.DocumentId,
                        sourceVersion.VersionId,
                        sourceVersion.StoragePath,
                        sourceVersion.FileName,
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
                        const string errorMessage = "OCR não retornou PDF válido.";

                        await jobs.MarkErrorAsync(job.Id, errorMessage, stoppingToken);

                        await SaveOcrDescriptionAndMetadataAsync(
                            db,
                            job.TenantId,
                            sourceVersion.DocumentId,
                            actorId,
                            "",
                            "OCR_ERROR",
                            stoppingToken);

                        await InsertDocumentAuditAsync(
                            db,
                            job.TenantId,
                            sourceVersion.DocumentId,
                            actorId,
                            "OCR_ERROR",
                            "OCR",
                            null,
                            new
                            {
                                jobId = job.Id,
                                error = errorMessage
                            },
                            "OCR_WORKER",
                            stoppingToken);

                        continue;
                    }

                    var extractedText = result.ExtractedText ?? "";
                    var newFileName = BuildOcrFileName(sourceVersion.FileName);

                    await using var ms = new MemoryStream(result.OcrPdfBytes);
                    ms.Position = 0;

                    await jobs.RenewLeaseAsync(job.Id, stoppingToken);

                    var addResult = await app.AddVersionWithOcrAsyncBackground(
                        tenantId: job.TenantId,
                        actorId: actorId,
                        documentId: sourceVersion.DocumentId,
                        content: ms,
                        fileName: newFileName,
                        contentType: "application/pdf",
                        ocrText: extractedText,
                        ip: "worker",
                        userAgent: "OcrWorker",
                        ct: stoppingToken);

                    if (!addResult.Success)
                    {
                        var errorMessage = addResult.Error?.Message ?? "Falha ao salvar versão OCR.";

                        await jobs.MarkErrorAsync(job.Id, errorMessage, stoppingToken);

                        await SaveOcrDescriptionAndMetadataAsync(
                            db,
                            job.TenantId,
                            sourceVersion.DocumentId,
                            actorId,
                            extractedText,
                            "OCR_VERSION_SAVE_ERROR",
                            stoppingToken);

                        await InsertDocumentAuditAsync(
                            db,
                            job.TenantId,
                            sourceVersion.DocumentId,
                            actorId,
                            "OCR_ERROR",
                            "OCR",
                            null,
                            new
                            {
                                jobId = job.Id,
                                error = errorMessage
                            },
                            "OCR_WORKER",
                            stoppingToken);

                        continue;
                    }

                    var ocrVersionId = addResult.Value;

                    await SaveOcrDescriptionAndMetadataAsync(
                        db,
                        job.TenantId,
                        sourceVersion.DocumentId,
                        actorId,
                        extractedText,
                        "OCR_COMPLETED",
                        stoppingToken);

                    await jobs.RenewLeaseAsync(job.Id, stoppingToken);

                    await RunClassificationAsync(
                        scope,
                        job,
                        sourceVersion.DocumentId,
                        ocrVersionId,
                        actorId,
                        extractedText,
                        stoppingToken);

                    await jobs.MarkCompletedAsync(
                        job.Id,
                        sourceVersion.DocumentId,
                        ocrVersionId,
                        stoppingToken);

                    await InsertDocumentAuditAsync(
                        db,
                        job.TenantId,
                        sourceVersion.DocumentId,
                        actorId,
                        "OCR_COMPLETED",
                        "OCR",
                        null,
                        new
                        {
                            jobId = job.Id,
                            sourceVersionId = job.DocumentVersionId,
                            resultVersionId = ocrVersionId,
                            extractedTextLength = extractedText.Length,
                            descriptionGenerated = true,
                            metadataGenerated = true
                        },
                        "OCR_WORKER",
                        stoppingToken);

                    _logger.LogInformation(
                        "OCR concluído. JobId={JobId}, DocumentId={DocumentId}, NewVersion={NewVersionId}",
                        job.Id,
                        sourceVersion.DocumentId,
                        ocrVersionId);

                    await Task.Delay(TimeSpan.FromMilliseconds(300), stoppingToken);
                }
                catch (PdfHasDigitalSignatureException)
                {
                    const string message = "Documento possui assinatura digital. Reexecute com FORÇAR OCR.";

                    await jobs.MarkErrorAsync(job.Id, message, stoppingToken);

                    await SaveOcrDescriptionAndMetadataAsync(
                        db,
                        job.TenantId,
                        sourceVersion.DocumentId,
                        actorId,
                        "",
                        "OCR_SIGNED_DOCUMENT_ERROR",
                        stoppingToken);

                    await InsertDocumentAuditAsync(
                        db,
                        job.TenantId,
                        sourceVersion.DocumentId,
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

                    await SaveOcrDescriptionAndMetadataAsync(
                        db,
                        job.TenantId,
                        sourceVersion.DocumentId,
                        actorId,
                        "",
                        "OCR_UNEXPECTED_ERROR",
                        stoppingToken);

                    await InsertDocumentAuditAsync(
                        db,
                        job.TenantId,
                        sourceVersion.DocumentId,
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
                ocrText: extractedText ?? "",
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

            var suggestedTypeId = classified.SuggestedTypeId ?? classified.DocumentTypeId;
            var suggestedConfidence = classified.SuggestedConfidence ?? classified.Confidence;

            if (suggestedTypeId.HasValue && suggestedTypeId.Value != Guid.Empty)
            {
                await classCommands.SaveSuggestionOnlyAsync(
                    tenantId: job.TenantId,
                    documentId: documentId,
                    suggestedTypeId: suggestedTypeId.Value,
                    suggestedConfidence: suggestedConfidence,
                    suggestedSummary: classified.Summary,
                    ct: ct);

                logger.LogInformation(
                    "Sugestão OCR gravada. Doc={DocId}, Version={VersionId}, SuggestedTypeId={SuggestedTypeId}, Confidence={Confidence}",
                    documentId,
                    newVersionId,
                    suggestedTypeId.Value,
                    suggestedConfidence);
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

    private static async Task SaveOcrDescriptionAndMetadataAsync(
        IDbConnectionFactory db,
        Guid tenantId,
        Guid documentId,
        Guid actorId,
        string? extractedText,
        string originAction,
        CancellationToken ct)
    {
        var description = BuildDescription(extractedText);
        var normalizedText = NormalizeText(extractedText);

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
                new
                {
                    tenantId,
                    documentId
                },
                cancellationToken: ct)) ?? "{}";

        const string updateDocumentSql = @"
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

        var updatedRows = await conn.ExecuteAsync(
            new CommandDefinition(
                updateDocumentSql,
                new
                {
                    tenantId,
                    documentId,
                    actorId,
                    description
                },
                cancellationToken: ct));

        await UpsertOcrMetadataAsync(
            conn,
            tenantId,
            documentId,
            "descricao_ocr",
            description,
            0.90m,
            ct);

        await UpsertOcrMetadataAsync(
            conn,
            tenantId,
            documentId,
            "ocr_resumo",
            description,
            0.90m,
            ct);

        await UpsertOcrMetadataAsync(
            conn,
            tenantId,
            documentId,
            "ocr_texto_inicial",
            string.IsNullOrWhiteSpace(normalizedText)
                ? "OCR executado sem texto legível suficiente."
                : normalizedText.Length <= 1000
                    ? normalizedText
                    : normalizedText[..1000] + "...",
            0.80m,
            ct);

        await InsertDocumentAuditAsync(
            db,
            tenantId,
            documentId,
            actorId,
            updatedRows > 0
                ? "DOCUMENT_DESCRIPTION_UPDATED_FROM_OCR"
                : "DOCUMENT_DESCRIPTION_METADATA_SAVED_FROM_OCR",
            "OCR",
            beforeJson,
            new
            {
                documentId,
                description,
                originAction,
                documentDescriptionUpdated = updatedRows > 0,
                metadataSaved = true
            },
            "OCR_WORKER",
            ct);
    }

    private static async Task UpsertOcrMetadataAsync(
        System.Data.IDbConnection conn,
        Guid tenantId,
        Guid documentId,
        string key,
        string value,
        decimal confidence,
        CancellationToken ct)
    {
        const string deleteSql = @"
DELETE FROM ged.document_metadata
WHERE tenant_id = @tenantId
  AND document_id = @documentId
  AND lower(key) = lower(@key)
  AND method = 'OCR';";

        await conn.ExecuteAsync(
            new CommandDefinition(
                deleteSql,
                new
                {
                    tenantId,
                    documentId,
                    key
                },
                cancellationToken: ct));

        const string insertSql = @"
INSERT INTO ged.document_metadata
(
    id,
    tenant_id,
    document_id,
    key,
    value,
    confidence,
    method,
    extracted_at
)
VALUES
(
    gen_random_uuid(),
    @tenantId,
    @documentId,
    @key,
    @value,
    @confidence,
    'OCR',
    now()
);";

        await conn.ExecuteAsync(
            new CommandDefinition(
                insertSql,
                new
                {
                    tenantId,
                    documentId,
                    key,
                    value,
                    confidence
                },
                cancellationToken: ct));
    }

    private static string BuildDescription(string? text)
    {
        var clean = NormalizeText(text);

        if (string.IsNullOrWhiteSpace(clean))
        {
            return "Documento processado por OCR. Não foi possível extrair texto legível suficiente, mas o processamento foi executado.";
        }

        if (clean.Length <= 500)
            return clean;

        return clean[..500] + "...";
    }

    private static string NormalizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        return string.Join(
            " ",
            text.Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("\t", " ")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
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

        var beforeJson = NormalizeJson(before);
        var afterJson = NormalizeJson(after);

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

    private static string NormalizeJson(object? value)
    {
        if (value is null)
            return "{}";

        if (value is string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return "{}";

            try
            {
                using var _ = JsonDocument.Parse(s);
                return s;
            }
            catch
            {
                return JsonSerializer.Serialize(new { value = s });
            }
        }

        return JsonSerializer.Serialize(value);
    }

    private static string BuildOcrFileName(string originalFileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(originalFileName);

        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "documento";

        if (baseName.EndsWith("_OCR", StringComparison.OrdinalIgnoreCase))
            return $"{baseName}.pdf";

        return $"{baseName}_OCR.pdf";
    }

    private static bool IsPdf(string? contentType, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(contentType) &&
            contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase))
            return true;

        return fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
    }
}