using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using InovaGed.Application;
using InovaGed.Application.Common.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InovaGed.Infrastructure.Preview;

public sealed class LibreOfficePreviewGenerator : IPreviewGenerator
{
    // ✅ LO no Windows: serializa (evita instabilidade do soffice)
    private static readonly SemaphoreSlim _loSemaphore = new(1, 1);

    // ✅ Single-flight por versão (evita 2 requests gerarem o mesmo preview)
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _previewLocks = new();

    private readonly IFileStorage _storage;
    private readonly ILogger<LibreOfficePreviewGenerator> _logger;
    private readonly LibreOfficeOptions _opt;

    public LibreOfficePreviewGenerator(
        IFileStorage storage,
        ILogger<LibreOfficePreviewGenerator> logger,
        IOptions<LibreOfficeOptions> opt)
    {
        _storage = storage;
        _logger = logger;
        _opt = opt.Value;
    }

    public async Task<string> GetOrCreatePreviewPdfAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        string sourceStoragePath,
        string originalFileName,
        CancellationToken ct)
    {
        // ✅ Preview é demorado: usa timeout interno (não RequestAborted)
        var minutes = _opt.TimeoutMinutes <= 0 ? 5 : _opt.TimeoutMinutes;
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(minutes));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token);
        var token = linked.Token;

        // path final do preview (determinístico)
        const string derivedPdfName = "preview.pdf";
        var storagePath = $"{tenantId:N}/previews/{documentId:N}/{versionId:N}/{derivedPdfName}";

        // ✅ cache: se já existe, não gera de novo
        if (await _storage.ExistsAsync(storagePath, token))
        {
            _logger.LogDebug("Preview já existe, reutilizando. Path={Path}", storagePath);
            return storagePath;
        }

        // ✅ single-flight por versão (evita 2 requisições simultâneas)
        var lockKey = $"{tenantId:N}:{documentId:N}:{versionId:N}";
        var sem = _previewLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

        await sem.WaitAsync(token);
        try
        {
            // recheck após lock
            if (await _storage.ExistsAsync(storagePath, token))
            {
                _logger.LogDebug("Preview já existe (após lock), reutilizando. Path={Path}", storagePath);
                return storagePath;
            }

            // ✅ LO serializado globalmente
            await _loSemaphore.WaitAsync(token);
            try
            {
                var sw = Stopwatch.StartNew();

                var baseTemp = Path.Combine(Path.GetTempPath(), "inovaged-preview");
                Directory.CreateDirectory(baseTemp);

                var workId = Guid.NewGuid().ToString("N");
                var workDir = Path.Combine(baseTemp, workId);

                var inDir = Path.Combine(workDir, "in");
                var outDir = Path.Combine(workDir, "out");
                var profileDir = Path.Combine(workDir, "lo-profile"); // ✅ profile isolado por job

                Directory.CreateDirectory(inDir);
                Directory.CreateDirectory(outDir);
                Directory.CreateDirectory(profileDir);

                var ext = Path.GetExtension(originalFileName);
                if (string.IsNullOrWhiteSpace(ext)) ext = ".bin";

                var inputPath = Path.Combine(inDir, "input" + ext);

                try
                {
                    // 1) baixa original para temp
                    await using (var src = await _storage.OpenReadAsync(sourceStoragePath, token))
                    await using (var fs = new FileStream(inputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await src.CopyToAsync(fs, token);
                    }

                    // 2) executa LibreOffice
                    var soffice = ResolveSofficePath();

                    var args = new List<string>
                    {
                        "--headless",
                        "--invisible",
                        "--nologo",
                        "--nofirststartwizard",
                        "--norestore",
                        "--convert-to", "pdf",
                        "--outdir", outDir,
                        inputPath
                    };

                    var (exit, stdout, stderr) = await RunProcessAsync(
                        fileName: soffice,
                        args: args,
                        profileDir: profileDir,
                        ct: token);

                    if (exit != 0)
                    {
                        _logger.LogError(
                            "LibreOffice falhou. ExitCode={Exit}. Out={Out}. Err={Err}. In={In} OutDir={OutDir}",
                            exit, stdout, stderr, inputPath, outDir);

                        throw new Exception("Falha ao converter arquivo para PDF.");
                    }

                    // 3) acha o PDF gerado
                    var generatedPdf = Directory.GetFiles(outDir, "*.pdf")
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .FirstOrDefault();

                    if (generatedPdf is null || !File.Exists(generatedPdf))
                    {
                        _logger.LogError(
                            "LibreOffice terminou mas nenhum PDF foi encontrado. Out={Out}. Err={Err}. OutDir={OutDir}",
                            stdout, stderr, outDir);

                        throw new Exception("Falha ao converter arquivo para PDF (PDF não gerado).");
                    }

                    // ✅ 4) Copia pra um arquivo intermediário que não está preso pelo LO
                    var stablePdf = Path.Combine(workDir, "final.pdf");
                    File.Copy(generatedPdf, stablePdf, overwrite: true);

                    // 5) salva no storage (com retry p/ lock momentâneo)
                    await using var pdfStream = new FileStream(
                        stablePdf,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite); // ✅ tolerante em Windows

                    await SaveDerivedWithRetryAsync(
                        storagePath,
                        pdfStream,
                        "application/pdf",
                        token);

                    sw.Stop();
                    _logger.LogInformation(
                        "Preview gerado e salvo. Tenant={Tenant} Doc={Doc} Ver={Ver} Path={Path} ElapsedMs={Ms}",
                        tenantId, documentId, versionId, storagePath, sw.ElapsedMilliseconds);

                    return storagePath;
                }
                catch (OperationCanceledException oce) when (timeoutCts.IsCancellationRequested)
                {
                    _logger.LogError(oce, "Preview cancelado por timeout interno. WorkDir={WorkDir}", workDir);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao gerar preview PDF via LibreOffice. WorkDir={WorkDir}", workDir);
                    throw;
                }
                finally
                {
                    // limpa temp (best-effort)
                    try { if (Directory.Exists(workDir)) Directory.Delete(workDir, true); }
                    catch { /* ignore */ }
                }
            }
            finally
            {
                _loSemaphore.Release();
            }
        }
        finally
        {
            sem.Release();

            // limpeza best-effort do dicionário para não crescer pra sempre
            // (só remove se não tem ninguém esperando)
            if (sem.CurrentCount == 1)
                _previewLocks.TryRemove(lockKey, out _);
        }
    }

    private async Task SaveDerivedWithRetryAsync(
        string storagePath,
        Stream content,
        string contentType,
        CancellationToken ct)
    {
        var delaysMs = new[] { 120, 250, 450, 800 };

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (content.CanSeek) content.Position = 0;
                await _storage.SaveDerivedAsync(storagePath, content, contentType, ct);
                return;
            }
            catch (IOException) when (attempt < delaysMs.Length)
            {
                await Task.Delay(delaysMs[attempt], ct);
            }
            catch (UnauthorizedAccessException) when (attempt < delaysMs.Length)
            {
                await Task.Delay(delaysMs[attempt], ct);
            }
        }
    }

    private string ResolveSofficePath()
    {
        if (!string.IsNullOrWhiteSpace(_opt.SofficePath) && File.Exists(_opt.SofficePath))
            return _opt.SofficePath;

        var p1 = @"C:\Program Files\LibreOffice\program\soffice.exe";
        var p2 = @"C:\Program Files (x86)\LibreOffice\program\soffice.exe";

        if (File.Exists(p1)) return p1;
        if (File.Exists(p2)) return p2;

        return "soffice";
    }

    private static string ToFileUri(string path)
    {
        var full = Path.GetFullPath(path).Replace("\\", "/");
        if (!full.StartsWith("/")) full = "/" + full;
        return "file://" + full;
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> args,
        string profileDir,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        // ✅ NO WINDOWS: define como variável de ambiente
        psi.Environment["UserInstallation"] = ToFileUri(profileDir);

        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var p = new Process { StartInfo = psi };

        var sbOut = new StringBuilder();
        var sbErr = new StringBuilder();

        p.Start();

        var readOut = Task.Run(async () =>
        {
            while (!p.StandardOutput.EndOfStream)
                sbOut.AppendLine(await p.StandardOutput.ReadLineAsync());
        }, CancellationToken.None);

        var readErr = Task.Run(async () =>
        {
            while (!p.StandardError.EndOfStream)
                sbErr.AppendLine(await p.StandardError.ReadLineAsync());
        }, CancellationToken.None);

        await Task.WhenAll(p.WaitForExitAsync(ct), readOut, readErr);

        return (p.ExitCode, sbOut.ToString(), sbErr.ToString());
    }
}

public sealed class LibreOfficeOptions
{
    public string? SofficePath { get; set; }
    public int TimeoutMinutes { get; set; } = 5;
}
