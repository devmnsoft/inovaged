using InovaGed.Application;
using InovaGed.Application.Classification;
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
    // GUID fixo para ações automáticas do OCR Worker
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

                var job = await jobs.DequeueAndMarkProcessingAsync(stoppingToken);
                if (job is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }

                var actorId = job.RequestedBy ?? SystemUsers.OcrWorker;

                _logger.LogInformation(
                    "Processando OCR JobId={JobId}, VersionId={VersionId}, Actor={ActorId}",
                    job.Id, job.DocumentVersionId, actorId);

                await jobs.RenewLeaseAsync(job.Id, stoppingToken);

                var v = await docs.GetVersionForDownloadAsync(job.TenantId, job.DocumentVersionId, stoppingToken);
                if (v is null)
                {
                    await jobs.MarkErrorAsync(job.Id, "Versão não encontrada.", stoppingToken);
                    continue;
                }

                await jobs.RenewLeaseAsync(job.Id, stoppingToken);

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
                        continue;
                    }

                    var newFileName = $"{Path.GetFileNameWithoutExtension(v.FileName)}_OCR.pdf";

                    await using var ms = new MemoryStream(result.OcrPdfBytes);
                    ms.Position = 0;

                    await jobs.RenewLeaseAsync(job.Id, stoppingToken);

                    // ✅ NÃO depende de usuário autenticado (worker não tem HttpContext)
                    var add = await app.AddVersionWithOcrAsyncBackground(
                        tenantId: job.TenantId,
                        actorId: actorId,
                        documentId: v.DocumentId,
                        content: ms,
                        fileName: newFileName,
                        contentType: "application/pdf",
                        ocrText: result.ExtractedText ?? "",
                        ip: "worker",
                        userAgent: "OcrWorker",
                        ct: stoppingToken);

                    if (!add.Success)
                    {
                        await jobs.MarkErrorAsync(job.Id, add.Error?.Message ?? "Falha ao salvar versão OCR.", stoppingToken);
                        continue;
                    }

                    // classificação automática (não bloqueia OCR)
                    try
                    {
                        var classifier = scope.ServiceProvider.GetRequiredService<IDocumentClassifier>();
                        var classRepo = scope.ServiceProvider.GetRequiredService<IDocumentClassificationRepository>();

                        var classified = await classifier.ClassifyAsync(
                            tenantId: job.TenantId,
                            documentId: v.DocumentId,
                            documentVersionId: add.Value,
                            ocrText: result.ExtractedText ?? "",
                            ct: stoppingToken);

                        await classRepo.UpsertClassificationAsync(
                            tenantId: job.TenantId,
                            documentId: v.DocumentId,
                            documentVersionId: add.Value,
                            documentTypeId: classified.DocumentTypeId,
                            confidence: classified.Confidence,
                            method: classified.Method,
                            summary: classified.Summary,
                            classifiedBy: actorId, // ✅ registra ator
                            ct: stoppingToken);

                        await classRepo.UpsertTagsAsync(
                            tenantId: job.TenantId,
                            documentId: v.DocumentId,
                            tags: classified.Tags,
                            method: classified.Method,
                            assignedBy: actorId, // ✅ registra ator
                            ct: stoppingToken);

                        await classRepo.UpsertMetadataAsync(
                            tenantId: job.TenantId,
                            documentId: v.DocumentId,
                            metadata: classified.Metadata,
                            method: classified.Method,
                            ct: stoppingToken);

                        _logger.LogInformation("Classificação automática aplicada. Doc={DocId}, Ver={VerId}", v.DocumentId, add.Value);
                    }
                    catch (Exception exClassify)
                    {
                        _logger.LogWarning(exClassify, "Falha na classificação automática (não bloqueia OCR). Doc={DocId}", v.DocumentId);
                    }

                    await jobs.MarkCompletedAsync(job.Id, stoppingToken);
                    _logger.LogInformation("OCR concluído. JobId={JobId}, NewVersion={NewVersionId}", job.Id, add.Value);

                    await Task.Delay(TimeSpan.FromMilliseconds(300), stoppingToken);
                }
                catch (PdfHasDigitalSignatureException)
                {
                    await jobs.MarkErrorAsync(job.Id, "Documento possui assinatura digital. Reexecute com FORÇAR OCR.", stoppingToken);
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

    private static bool IsPdf(string? ct, string name)
        => (!string.IsNullOrWhiteSpace(ct) && ct.Contains("pdf", StringComparison.OrdinalIgnoreCase))
           || name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
}