using System.Diagnostics;
using System.Text;
using InovaGed.Application.Ocr;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Ocr;

public sealed class OcrMyPdfProcessor : IOcrProcessor
{
    private readonly ILogger<OcrMyPdfProcessor> _logger;

    // Ajuste se quiser outro diretório temporário
    private static readonly string TempRoot = Path.Combine(Path.GetTempPath(), "inovaged-ocr");

    public OcrMyPdfProcessor(ILogger<OcrMyPdfProcessor> logger)
    {
        _logger = logger;
        Directory.CreateDirectory(TempRoot);
    }

    public async Task<OcrResult> ProcessAsync(OcrJobDto job, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(job.SourcePdfPath))
            throw new InvalidOperationException("SourcePdfPath não informado no job OCR.");

        if (!File.Exists(job.SourcePdfPath))
            throw new FileNotFoundException("Arquivo de origem para OCR não encontrado.", job.SourcePdfPath);

        var workDir = Path.Combine(TempRoot, job.Id.ToString("N"));
        Directory.CreateDirectory(workDir);

        var inputPdf = job.SourcePdfPath;
        var outputPdf = Path.Combine(workDir, $"ocr_{job.VersionId:N}.pdf");

        // 1) Rodar OCRmyPDF para gerar PDF pesquisável
        // Requisitos no Windows:
        // - Python instalado
        // - ocrmypdf instalado (pip install ocrmypdf)
        // - Tesseract instalado (no PATH)
        // - Ghostscript instalado (recomendado)
        var lang = string.IsNullOrWhiteSpace(job.Language) ? "por" : job.Language.Trim();

        // --force-ocr garante OCR mesmo se já tiver texto
        // --skip-text evita re-OCR em páginas que já tem texto (se quiser)
        // --deskew melhora scans tortos
        // --rotate-pages tenta corrigir rotação
        // --optimize 3 otimiza o PDF
        var args = $"-m ocrmypdf --force-ocr --deskew --rotate-pages --optimize 3 -l {EscapeArg(lang)} {EscapeArg(inputPdf)} {EscapeArg(outputPdf)}";

        _logger.LogInformation("OCRmyPDF: {Args}", args);

        var ocrStdout = new StringBuilder();
        var ocrStderr = new StringBuilder();

        var exit = await RunProcessAsync(
            fileName: "py",
            arguments: args,
            workingDirectory: workDir,
            stdout: ocrStdout,
            stderr: ocrStderr,
            ct: ct);

        if (exit != 0)
        {
            var err = ocrStderr.ToString();
            var outt = ocrStdout.ToString();
            throw new Exception($"OCRmyPDF falhou (ExitCode={exit}). STDERR={TrimMax(err, 4000)} OUT={TrimMax(outt, 4000)}");
        }

        if (!File.Exists(outputPdf))
            throw new Exception("OCRmyPDF concluiu sem gerar o PDF de saída.");

        // 2) Extrair texto do PDF pesquisável com pdftotext (Poppler)
        // Requisito:
        // - pdftotext.exe no PATH (Poppler for Windows)
        // Se você não tiver, eu te passo alternativa com iText/PdfPig depois.
        var txtPath = Path.Combine(workDir, $"ocr_{job.VersionId:N}.txt");

        var ptArgs = $"{EscapeArg(outputPdf)} {EscapeArg(txtPath)}";
        _logger.LogInformation("pdftotext: {Args}", ptArgs);

        var ptStdout = new StringBuilder();
        var ptStderr = new StringBuilder();

        var ptExit = await RunProcessAsync(
            fileName: "pdftotext",
            arguments: ptArgs,
            workingDirectory: workDir,
            stdout: ptStdout,
            stderr: ptStderr,
            ct: ct);

        string text = "";
        if (ptExit == 0 && File.Exists(txtPath))
        {
            text = await File.ReadAllTextAsync(txtPath, Encoding.UTF8, ct);
        }
        else
        {
            // Não falha o OCR por falta de texto extraído (mas loga)
            _logger.LogWarning("pdftotext falhou (ExitCode={Exit}). STDERR={Err}",
                ptExit, TrimMax(ptStderr.ToString(), 2000));
        }

        // 3) PageCount (opcional) — manter 0 por enquanto (sem depender de libs)
        var pageCount = 0;

        return new OcrResult
        {
            Text = text ?? string.Empty,
            OutputPdfPath = outputPdf,
            Language = lang,
            PageCount = pageCount
        };
    }

    private static async Task<int> RunProcessAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        StringBuilder stdout,
        StringBuilder stderr,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };

        p.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        if (!p.Start())
            throw new Exception($"Não foi possível iniciar o processo: {fileName}");

        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        await p.WaitForExitAsync(ct);
        return p.ExitCode;
    }

    private static string EscapeArg(string s)
    {
        if (string.IsNullOrEmpty(s)) return "\"\"";
        if (s.Contains(' ') || s.Contains('"'))
            return "\"" + s.Replace("\"", "\\\"") + "\"";
        return s;
    }

    private static string TrimMax(string s, int max)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max) + "...");
}
