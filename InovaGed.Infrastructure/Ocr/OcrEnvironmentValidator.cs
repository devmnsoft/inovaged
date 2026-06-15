using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using InovaGed.Application.Common.Storage;
using InovaGed.Application.Ocr;
using Microsoft.Extensions.Options;

namespace InovaGed.Infrastructure.Ocr;

public sealed class OcrEnvironmentValidator : IOcrEnvironmentValidator
{
    private readonly OcrOptions _options;
    private readonly IFileStorage _storage;

    public OcrEnvironmentValidator(IOptions<OcrOptions> options, IFileStorage storage)
    {
        _options = options.Value;
        _storage = storage;
    }

    public async Task<OcrEnvironmentReport> ValidateAsync(CancellationToken ct)
    {
        var checks = new List<OcrEnvironmentCheck>();
        var warnings = new List<string>();
        var storagePath = ResolveStoragePath();

        if ((_options.OcrMyPdfPath ?? "").Contains(@"\Users\Administrator\AppData\", StringComparison.OrdinalIgnoreCase))
            warnings.Add("O OCRmyPDF está instalado no perfil do Administrator. Em produção IIS, recomenda-se instalar Python/OCRmyPDF em caminho global, como C:\\Tools\\Python311 ou C:\\Program Files\\Python311, com permissão para o usuário do AppPool.");

        checks.Add(await CheckFileAsync("OcrMyPdfPath", _options.OcrMyPdfPath, "--version", ct));
        checks.Add(await CheckFileAsync("PythonPath", _options.PythonPath, "--version", ct));
        checks.Add(await CheckFileAsync("TesseractPath", _options.TesseractPath, "--version", ct));
        checks.Add(CheckDirectory("TesseractDataPath", _options.TesseractDataPath));
        checks.Add(CheckFileOnly("Tesseract por.traineddata", Path.Combine(_options.TesseractDataPath ?? "", "por.traineddata")));
        checks.Add(CheckFileOnly("Tesseract eng.traineddata", Path.Combine(_options.TesseractDataPath ?? "", "eng.traineddata")));
        checks.Add(CheckDirectory("GhostscriptBinPath", _options.GhostscriptBinPath));
        var gs = ResolveGhostscriptExe();
        checks.Add(await CheckFileAsync("Ghostscript", gs, "--version", ct));
        checks.Add(await CheckFileAsync("QpdfPath", _options.QpdfPath, "--version", ct));
        checks.Add(await CheckFileAsync("PdfToTextPath", _options.PdfToTextPath, "-v", ct));
        checks.Add(CheckDirectory("PopplerBinPath", _options.PopplerBinPath));
        checks.Add(CheckStorage(storagePath));

        var valid = checks.All(c => c.Exists && c.PermissionOk && (c.CommandResult is null || (c.CommandResult.ExitCode == 0 && !c.CommandResult.TimedOut)));
        return new OcrEnvironmentReport(valid, Environment.UserName, WindowsIdentity.GetCurrent().Name, storagePath, checks, warnings, DateTimeOffset.UtcNow);
    }

    private string ResolveStoragePath()
    {
        var type = _storage.GetType();
        foreach (var name in new[] { "RootPath", "BasePath", "StorageRoot", "Root" })
        {
            var prop = type.GetProperty(name);
            if (prop?.GetValue(_storage) is string value && !string.IsNullOrWhiteSpace(value)) return value;
        }
        return AppContext.BaseDirectory;
    }

    private OcrEnvironmentCheck CheckStorage(string path)
    {
        try
        {
            var exists = Directory.Exists(path);
            if (exists)
            {
                var probe = Path.Combine(path, $".ocr-permission-{Guid.NewGuid():N}.tmp");
                File.WriteAllText(probe, "ok", Encoding.UTF8);
                _ = File.ReadAllText(probe, Encoding.UTF8);
                File.Delete(probe);
            }
            return new("Storage", path, exists, exists, null, "write/read probe", null, exists ? null : "Pasta de storage não existe.");
        }
        catch (Exception ex) { return new("Storage", path, Directory.Exists(path), false, null, "write/read probe", null, ex.Message); }
    }

    private OcrEnvironmentCheck CheckDirectory(string name, string? path)
    {
        var p = path ?? "";
        var exists = Directory.Exists(p);
        return new(name, p, exists, exists, null, null, null, exists ? null : "Diretório não encontrado.");
    }

    private OcrEnvironmentCheck CheckFileOnly(string name, string path)
    {
        var exists = File.Exists(path);
        return new(name, path, exists, exists, null, null, null, exists ? null : "Arquivo não encontrado.");
    }

    private async Task<OcrEnvironmentCheck> CheckFileAsync(string name, string? path, string versionArgs, CancellationToken ct)
    {
        var p = path ?? "";
        if (!File.Exists(p)) return new(name, p, false, false, null, $"\"{p}\" {versionArgs}", null, "Arquivo não encontrado ou inacessível pelo usuário do processo.");
        try
        {
            await using (File.Open(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) { }
            var result = await RunVersionAsync(p, versionArgs, ct);
            var version = FirstLine(result.StdOut, result.StdErr);
            return new(name, p, true, result.ExitCode == 0 && !result.TimedOut, version, $"\"{p}\" {versionArgs}", result, result.ExitCode == 0 && !result.TimedOut ? null : "Comando de versão falhou.");
        }
        catch (Exception ex) { return new(name, p, true, false, null, $"\"{p}\" {versionArgs}", null, ex.Message); }
    }

    private string ResolveGhostscriptExe()
    {
        var dir = _options.GhostscriptBinPath ?? "";
        var x64 = Path.Combine(dir, "gswin64c.exe");
        if (File.Exists(x64)) return x64;
        return Path.Combine(dir, "gswin32c.exe");
    }

    private static string? FirstLine(params string[] values) => values.SelectMany(v => (v ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries)).Select(v => v.Trim()).FirstOrDefault();

    private static async Task<OcrCommandResult> RunVersionAsync(string file, string args, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var psi = new ProcessStartInfo(file, args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Falha ao iniciar processo.");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        try { await p.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch { } return new(null, await stdout, await stderr, sw.ElapsedMilliseconds, true); }
        return new(p.ExitCode, await stdout, await stderr, sw.ElapsedMilliseconds, false);
    }
}
