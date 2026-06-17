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
    private readonly IOcrProcessRunner _runner;

    public OcrEnvironmentValidator(IOptions<OcrOptions> options, IFileStorage storage, IOcrProcessRunner runner)
    {
        _options = options.Value;
        _storage = storage;
        _runner = runner;
    }

    public async Task<OcrEnvironmentValidationResult> ValidateAsync(CancellationToken ct)
    {
        var result = new OcrEnvironmentValidationResult
        {
            ProcessUser = Environment.UserName,
            WindowsIdentity = WindowsIdentity.GetCurrent().Name,
            MachineName = Environment.MachineName,
            CurrentDirectory = Directory.GetCurrentDirectory(),
            BaseDirectory = AppContext.BaseDirectory,
            StoragePath = ResolveStoragePath(),
            EffectivePath = BuildOcrPathEnvironment(_options),
            GeneratedAtUtc = DateTimeOffset.UtcNow
        };
        var env = new Dictionary<string, string> { ["PATH"] = result.EffectivePath };
        if (!string.IsNullOrWhiteSpace(_options.TesseractDataPath)) env["TESSDATA_PREFIX"] = _options.TesseractDataPath!;

        if ((_options.OcrMyPdfPath ?? string.Empty).Contains(@"\Users\Administrator\AppData\", StringComparison.OrdinalIgnoreCase))
            result.Warnings.Add("O OCRmyPDF está instalado dentro do perfil do usuário Administrator. Em ambiente IIS, o AppPool pode não ter acesso. Recomenda-se instalar em caminho global, por exemplo C:\\Tools\\Python311 ou C:\\Program Files\\Python311.");

        result.Checks.Add(await CheckExecutableAsync("OCRmyPDF", "Ocr:OcrMyPdfPath", _options.OcrMyPdfPath, new[] { "--version" }, env, ct));
        result.Checks.Add(await CheckExecutableAsync("Python", "Ocr:PythonPath", _options.PythonPath, new[] { "--version" }, env, ct));
        result.Checks.Add(await CheckExecutableAsync("Tesseract", "Ocr:TesseractPath", _options.TesseractPath, new[] { "--version" }, env, ct));
        result.Checks.Add(CheckDirectory("TesseractDataPath", "Ocr:TesseractDataPath", _options.TesseractDataPath, "Configure a pasta tessdata do Tesseract."));
        result.Checks.Add(CheckFile("Tesseract idioma português", "Ocr:TesseractDataPath", Path.Combine(_options.TesseractDataPath ?? string.Empty, "por.traineddata"), "Instale/copiei por.traineddata para a pasta tessdata."));
        result.Checks.Add(CheckFile("Tesseract idioma inglês", "Ocr:TesseractDataPath", Path.Combine(_options.TesseractDataPath ?? string.Empty, "eng.traineddata"), "Instale/copiei eng.traineddata para a pasta tessdata."));
        result.Checks.Add(CheckDirectory("GhostscriptBinPath", "Ocr:GhostscriptBinPath", _options.GhostscriptBinPath, "Configure a pasta bin do Ghostscript."));
        result.Checks.Add(await CheckExecutableAsync("Ghostscript", "Ocr:GhostscriptBinPath", ResolveGhostscriptExe(), new[] { "--version" }, env, ct));
        result.Checks.Add(await CheckExecutableAsync("qpdf", "Ocr:QpdfPath", _options.QpdfPath, new[] { "--version" }, env, ct));
        result.Checks.Add(await CheckExecutableAsync("Poppler pdftotext", "Ocr:PdfToTextPath", _options.PdfToTextPath, new[] { "-v" }, env, ct));
        result.Checks.Add(CheckDirectory("PopplerBinPath", "Ocr:PopplerBinPath", _options.PopplerBinPath, "Configure a pasta bin do Poppler."));
        result.Checks.Add(CheckStorage(result.StoragePath));

        result.IsValid = result.Checks.Where(c => c.Required).All(c => c.Success);
        var failed = result.Checks.Where(c => c.Required && !c.Success).Select(c => $"{c.Name}: {c.Message ?? c.Suggestion ?? "falhou"}").ToList();
        result.Summary = result.IsValid ? "Ambiente OCR válido." : "Ambiente OCR inválido: " + string.Join("; ", failed);
        return result;
    }

    public static string BuildOcrPathEnvironment(OcrOptions options)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parts = new[] { Path.GetDirectoryName(options.OcrMyPdfPath ?? string.Empty), Path.GetDirectoryName(options.PythonPath ?? string.Empty), options.GhostscriptBinPath, Path.GetDirectoryName(options.TesseractPath ?? string.Empty), options.PopplerBinPath, options.QpdfBinPath, Path.GetDirectoryName(options.QpdfPath ?? string.Empty), Environment.GetEnvironmentVariable("PATH") ?? string.Empty };
        return string.Join(Path.PathSeparator, parts.SelectMany(p => (p ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)).Select(p => p.Trim()).Where(p => !string.IsNullOrWhiteSpace(p) && seen.Add(p)));
    }

    private async Task<OcrEnvironmentCheckResult> CheckExecutableAsync(string name, string configKey, string? path, IReadOnlyList<string> args, IDictionary<string, string> env, CancellationToken ct)
    {
        var exists = !string.IsNullOrWhiteSpace(path) && (!Path.IsPathRooted(path) || File.Exists(path));
        var cmd = $"\"{path}\" {string.Join(' ', args)}";
        if (!exists)
            return Check(name, configKey, path, false, false, cmd, null, "Arquivo não encontrado ou caminho não configurado.", SuggestExecutable(path));
        var proc = await _runner.RunVersionAsync(path!, args, AppContext.BaseDirectory, env, TimeSpan.FromSeconds(10), ct);
        return Check(name, configKey, path, true, proc.Success, cmd, proc, proc.Success ? null : proc.ExceptionMessage ?? "Comando de versão falhou.", proc.Success ? null : SuggestExecutable(path));
    }

    private static OcrEnvironmentCheckResult Check(string name, string? key, string? path, bool exists, bool success, string? cmd, OcrProcessResult? proc, string? message, string? suggestion) => new() { Name = name, ConfigKey = key, Path = path, Required = true, Exists = exists, CanExecute = proc?.Success ?? success, Success = success, VersionCommand = cmd, ExitCode = proc?.ExitCode, StdOut = proc?.StdOut, StdErr = proc?.StdErr, ElapsedMs = proc?.ElapsedMs ?? 0, ProcessResult = proc, Message = message, Suggestion = suggestion };
    private static OcrEnvironmentCheckResult CheckDirectory(string name, string key, string? path, string suggestion) { var ok = !string.IsNullOrWhiteSpace(path) && Directory.Exists(path); return Check(name, key, path, ok, ok, null, null, ok ? null : "Diretório não encontrado.", suggestion); }
    private static OcrEnvironmentCheckResult CheckFile(string name, string key, string path, string suggestion) { var ok = File.Exists(path); return Check(name, key, path, ok, ok, null, null, ok ? null : "Arquivo não encontrado.", suggestion); }
    private OcrEnvironmentCheckResult CheckStorage(string path) { try { var ok = Directory.Exists(path); if (ok) { var f = Path.Combine(path, $".ocr-permission-{Guid.NewGuid():N}.tmp"); File.WriteAllText(f, "ok", Encoding.UTF8); File.Delete(f); } return Check("Storage", "Storage:Local:RootPath", path, ok, ok, "criar/apagar arquivo temporário", null, ok ? null : "Pasta de storage não existe.", "Crie a pasta e conceda leitura/escrita ao AppPool."); } catch (Exception ex) { return Check("Storage", "Storage:Local:RootPath", path, Directory.Exists(path), false, "criar/apagar arquivo temporário", null, ex.Message, "Conceda permissão de leitura/escrita ao usuário do AppPool."); } }
    private string ResolveStoragePath() { foreach (var name in new[] { "RootPath", "BasePath", "StorageRoot", "Root" }) if (_storage.GetType().GetProperty(name)?.GetValue(_storage) is string v && !string.IsNullOrWhiteSpace(v)) return v; return AppContext.BaseDirectory; }
    private string ResolveGhostscriptExe() { var dir = _options.GhostscriptBinPath ?? string.Empty; var x64 = Path.Combine(dir, "gswin64c.exe"); return File.Exists(x64) ? x64 : Path.Combine(dir, "gswin32c.exe"); }
    private static string SuggestExecutable(string? path) => (path ?? string.Empty).Contains(@"\Users\Administrator\AppData\", StringComparison.OrdinalIgnoreCase) ? "Conceda permissão de leitura/execução ao usuário do AppPool ou reinstale Python/OCRmyPDF em caminho global." : "Corrija o caminho configurado e garanta permissão de leitura/execução ao usuário do AppPool.";
}
