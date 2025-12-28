using InovaGed.Application;
using InovaGed.Application.Common.Storage;
using InovaGed.Application.Documents;
using InovaGed.Application.Ocr;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ocr;

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
                var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
                var preview = scope.ServiceProvider.GetRequiredService<IPreviewGenerator>();
                var ocr = scope.ServiceProvider.GetRequiredService<IOcrService>();
                var app = scope.ServiceProvider.GetRequiredService<DocumentAppService>();

                // tenta pegar 1 job pendente
                var job = await jobs.DequeueAndMarkProcessingAsync(stoppingToken);
                if (job is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                _logger.LogInformation("Processando OCR JobId={JobId}, VersionId={VersionId}", job.Id, job.DocumentVersionId);

                // carrega versão (precisa de tenant)
                var v = await docs.GetVersionForDownloadAsync(job.TenantId, job.DocumentVersionId, stoppingToken);
                if (v is null)
                {
                    await jobs.MarkErrorAsync(job.Id, "Versão não encontrada.", stoppingToken);
                    continue;
                }

                // determina PDF base (PDF original ou preview convertido)
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

                // roda OCR
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

                    // cria nova versão OCR (indexa texto OCR real)
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
                        userAgent: "OcrWorker",
                        ct: stoppingToken);

                    if (!add.Success)
                    {
                        await jobs.MarkErrorAsync(job.Id, add.Error?.Message ?? "Falha ao salvar versão OCR.", stoppingToken);
                        continue;
                    }

                    await jobs.MarkCompletedAsync(job.Id, stoppingToken);
                    _logger.LogInformation("OCR concluído. JobId={JobId}, NewVersion={NewVersionId}", job.Id, add.Value);
                }
                catch (PdfHasDigitalSignatureException)
                {
                    // aqui o OCR não pode seguir sem force
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
