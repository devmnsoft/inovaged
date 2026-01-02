using InovaGed.Application;
using InovaGed.Application.Documents;
using InovaGed.Application.Ocr;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ocr;

public sealed class OcrBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OcrBackgroundService> _logger;

    public OcrBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<OcrBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OCR BackgroundService iniciado.");

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

                // ✅ Pega job pendente já marcando PROCESSING
                var job = await jobs.DequeueAndMarkProcessingAsync(stoppingToken);

                if (job is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
                    continue;
                }

                _logger.LogInformation("Processando OCR JobId={JobId}, VersionId={VersionId}",
                    job.Id, job.DocumentVersionId);

                // ✅ Busca dados da versão
                var v = await docs.GetVersionForDownloadAsync(job.TenantId, job.DocumentVersionId, stoppingToken);

                if (v is null)
                {
                    await jobs.MarkErrorAsync(job.Id, "Versão não encontrada para OCR.", stoppingToken);
                    continue;
                }

                // ✅ Garante PDF (se não for, gera preview com LibreOffice)
                var pdfStoragePath = IsPdf(v.ContentType, v.FileName)
                    ? v.StoragePath
                    : await preview.GetOrCreatePreviewPdfAsync(
                        job.TenantId,
                        v.DocumentId,
                        v.VersionId,
                        v.StoragePath,
                        v.FileName,
                        stoppingToken);

                // ✅ Executa OCR (OCRmyPDF/Tesseract)
                try
                {
                    var result = await ocr.OcrizePdfAsync(
                        pdfStoragePath: pdfStoragePath,
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

                    var add = await app.AddVersionWithOcrAsync(
                        documentId: v.DocumentId,
                        content: ms,
                        fileName: newFileName,
                        contentType: "application/pdf",
                        ocrText: result.ExtractedText ?? "",
                        ip: "worker",
                        userAgent: "OcrBackgroundService",
                        ct: stoppingToken);

                    if (!add.Success)
                    {
                        await jobs.MarkErrorAsync(job.Id, add.Error?.Message ?? "Falha ao salvar versão OCR.", stoppingToken);
                        continue;
                    }

                    await jobs.MarkCompletedAsync(job.Id, stoppingToken);

                    _logger.LogInformation("OCR concluído. JobId={JobId}, NewVersion={NewVersionId}",
                        job.Id, add.Value);
                }
                catch (PdfHasDigitalSignatureException)
                {
                    await jobs.MarkErrorAsync(job.Id,
                        "Documento possui assinatura digital. Reexecute com opção de invalidar assinatura/forçar OCR.",
                        stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha no OCR BackgroundService.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }

        _logger.LogInformation("OCR BackgroundService finalizado.");
    }

    private static bool IsPdf(string? contentType, string fileName)
        => (!string.IsNullOrWhiteSpace(contentType) && contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase))
           || fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
}
