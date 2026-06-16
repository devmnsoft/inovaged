using System.Diagnostics;
using InovaGed.Application.Common.Storage;
using InovaGed.Application.Documents;
using InovaGed.Application.Ocr;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InovaGed.Infrastructure.Ocr;

public sealed class OcrWorkerHostedService : BackgroundService
{
    private readonly ILogger<OcrWorkerHostedService> _logger;
    private readonly IOcrJobRepository _jobs;
    private readonly IDocumentQueries _docs;
    private readonly IFileStorage _storage;
    private readonly IDocumentSearchIndex _searchIndex;
    private readonly OcrOptions _opt;

    public OcrWorkerHostedService(
        ILogger<OcrWorkerHostedService> logger,
        IOcrJobRepository jobs,
        IDocumentQueries docs,
        IFileStorage storage,
        IDocumentSearchIndex searchIndex,
        IOptions<OcrOptions> opt)
    {
        _logger = logger;
        _jobs = jobs;
        _docs = docs;
        _storage = storage;
        _searchIndex = searchIndex;
        _opt = opt.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OCR Worker iniciado.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var lease = await _jobs.LeaseNextAsync(TimeSpan.FromMinutes(5), stoppingToken);
                if (lease is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1,_opt.WorkerDelaySeconds)), stoppingToken);
                    continue;
                }

                await ProcessJobAsync(lease, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { _logger.LogInformation("OCR Worker finalizado por cancelamento da aplicação."); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro geral no loop do OCR Worker.");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }

    private async Task ProcessJobAsync(OcrJobLease lease, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("OCR job lease: #{JobId} Tenant={TenantId} Version={Version}", lease.JobId, lease.TenantId, lease.DocumentVersionId);

        // pasta temp por job
        var outDir = Path.Combine(Path.GetTempPath(), "inovaged-ocr", lease.JobId.ToString());
        Directory.CreateDirectory(outDir);

        var inputTmp = Path.Combine(outDir, $"{lease.DocumentVersionId:N}-input");
        var outputPdf = Path.Combine(outDir, $"{lease.DocumentVersionId:N}-ocr.pdf");
        var sidecarTxt = Path.Combine(outDir, $"{lease.DocumentVersionId:N}-ocr.txt");

        try
        {
            var v = await _docs.GetVersionForDownloadAsync(lease.TenantId, lease.DocumentVersionId, ct);
            if (v is null)
                throw new InvalidOperationException("Versão do documento não encontrada.");

            if (!_opt.Enabled)
                throw new InvalidOperationException("OCR Worker está desabilitado em configuração.");

            var ocrMyPdfPath = _opt.OcrMyPdfPath;
            if (string.IsNullOrWhiteSpace(ocrMyPdfPath))
            {
                _logger.LogWarning("OCRmyPDF não configurado. Configure Ocr:OcrMyPdfPath.");
                throw new InvalidOperationException("Ocr:OcrMyPdfPath não configurado.");
            }

            if (!File.Exists(ocrMyPdfPath))
                throw new FileNotFoundException("Caminho do ocrmypdf.exe inválido", ocrMyPdfPath);

            // ✅ baixa do storage para um arquivo temporário local (sem GetPhysicalPathAsync)
            var ext = Path.GetExtension(v.FileName);
            if (!string.IsNullOrWhiteSpace(ext))
                inputTmp += ext;

            await DownloadToLocalTempAsync(v.StoragePath, inputTmp, ct);

            // roda OCR
            await RunOcrMyPdfAsync(ocrMyPdfPath, inputTmp, outputPdf, sidecarTxt, ct);

            // lê texto OCR
            var ocrText = File.Exists(sidecarTxt)
                ? await File.ReadAllTextAsync(sidecarTxt, ct)
                : null;

            // indexa no document_search
            await _searchIndex.UpsertOcrTextAsync(
                tenantId: lease.TenantId,
                documentId: v.DocumentId,
                versionId: lease.DocumentVersionId,
                fileName: v.FileName,
                ocrText: ocrText,
                ct: ct);

            // ✅ aqui corrige CS1739: não usa outputVersionId
            await _jobs.MarkCompletedAsync(lease.JobId, ct);

            _logger.LogInformation("OCR job concluído: #{JobId}", lease.JobId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("OCR job cancelado por shutdown. JobId={JobId} ElapsedMs={ElapsedMs}", lease.JobId, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            var attempts = 1;
            var maxAttempts = Math.Max(1, _opt.MaxAttempts);
            var retrySeconds = _opt.RetryBackoffSeconds?.Length > 0 ? _opt.RetryBackoffSeconds : new[] { 30, 120, 300 };
            var retryIndex = Math.Min(attempts - 1, retrySeconds.Length - 1);
            var shouldRetry = attempts < maxAttempts;

            _logger.LogWarning(ex, "OCR falhou. JobId={JobId} Tenant={TenantId} Version={VersionId} Attempts={Attempts}/{MaxAttempts} Retry={Retry} ElapsedMs={ElapsedMs}", lease.JobId, lease.TenantId, lease.DocumentVersionId, attempts, maxAttempts, shouldRetry, sw.ElapsedMilliseconds);

            if (await _jobs.IsCancelRequestedAsync(lease.JobId, ct))
            {
                await _jobs.MarkErrorAsync(lease.JobId, "Processamento cancelado pelo administrador.", ct);
                return;
            }

            if (shouldRetry)
            {
                await _jobs.MarkRetryAsync(lease.JobId, attempts, DateTimeOffset.UtcNow.AddSeconds(retrySeconds[retryIndex]), ex.Message[..Math.Min(300, ex.Message.Length)], ct);
                return;
            }

            await _jobs.MarkErrorAsync(lease.JobId, ex.Message[..Math.Min(300, ex.Message.Length)], ct);
        }
        finally
        {
            // limpeza leve (opcional)
            try { Directory.Delete(outDir, recursive: true); } catch { /* ignore */ }
        }
    }

    private async Task DownloadToLocalTempAsync(string storagePath, string localFile, CancellationToken ct)
    {
        // ✅ aqui a gente usa o método mais comum em storages: OpenReadAsync(path)
        // Se seu IFileStorage não tiver OpenReadAsync, me diga quais métodos ele tem.
        await using var input = await _storage.OpenReadAsync(storagePath, ct);
        await using var output = File.Create(localFile);
        await input.CopyToAsync(output, ct);
    }

    private static async Task RunOcrMyPdfAsync(string exe, string input, string output, string sidecar, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = $"--force-ocr --sidecar \"{sidecar}\" \"{input}\" \"{output}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("Não foi possível iniciar o processo ocrmypdf.");
        _ = await p.StandardOutput.ReadToEndAsync();
        var stdErr = await p.StandardError.ReadToEndAsync();

        await p.WaitForExitAsync(ct);

        if (p.ExitCode != 0)
            throw new InvalidOperationException($"ocrmypdf falhou (exit={p.ExitCode}). Erro: {stdErr}");
    }
}
