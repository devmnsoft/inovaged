using System.Text;
using InovaGed.Application;
using InovaGed.Application.Common.Storage;
using InovaGed.Application.Ocr;
using InovaGed.Infrastructure.Ocr;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InovaGed.Application.Tests;

public sealed class OcrTranscriptIntegrityTests
{
    private static string Fragmented => string.Join("\n", "DOCUMENTOSINTETICO".ToCharArray());

    [Fact]
    public async Task Fragmented_orientation_is_retried_once_from_original_without_cardinal_rotation()
    {
        var runner = new Runner { FirstText = Fragmented, Text = "Documento recuperado sem interpretação" };
        var result = await Service(runner).OcrizePdfAsync("inclinada.pdf", false, default);
        Assert.Equal(runner.Text, result.ExtractedText);
        Assert.Equal(2, runner.OcrCalls);
        Assert.DoesNotContain("--rotate-pages", runner.OcrArguments);
        Assert.Contains("--deskew", runner.OcrArguments);
        Assert.Contains("--tesseract-pagesegmode", runner.OcrArguments);
        Assert.Contains("-raw", runner.TextArguments);
        Assert.Equal("tesseract-psm6-no-rotation-poppler-raw", result.RecoveryMethod);
    }

    [Fact]
    public async Task Unrecoverable_fragmentation_requires_review_instead_of_success()
    {
        var runner = new Runner { Text = Fragmented };
        var error = await Assert.ThrowsAsync<OcrProcessingException>(() => Service(runner).OcrizePdfAsync("ruim.pdf", false, default));
        Assert.Equal(OcrFailureCode.OCR_TEXT_LOW_QUALITY, error.Code);
        Assert.True(error.IsPermanent);
        Assert.Equal(2, runner.OcrCalls);
        Assert.False(OcrTextQuality.IsFragmented("Tabela\n1\n2\n3\n4\n5\n6\n7\n8\n9\n0\n1\n2\n3\n4"));
    }

    [Theory]
    [InlineData(1, false, "texto", OcrFailureCode.OCR_TEXT_EXTRACTION_FAILED)]
    [InlineData(0, true, "texto", OcrFailureCode.OCR_TEXT_EXTRACTION_FAILED)]
    [InlineData(0, false, " \n\f", OcrFailureCode.OCR_TEXT_EMPTY)]
    public async Task Failed_or_empty_extraction_is_not_reported_as_success(int exit, bool timeout, string text, OcrFailureCode code)
    {
        var runner = new Runner { Exit = exit, Timeout = timeout, Text = text };
        var error = await Assert.ThrowsAsync<OcrProcessingException>(() => Service(runner).OcrizePdfAsync("original.pdf", false, default));
        Assert.Equal(code, error.Code);
        Assert.DoesNotContain("segredo", error.DetailsJson);
    }

    [Fact]
    public async Task Original_text_accents_and_signature_protection_are_preserved()
    {
        var runner = new Runner { Text = "Paciente: João da Conceição\nProcedimento: avaliação\fSegunda página" };
        var result = await Service(runner).OcrizePdfAsync("pasta com espaços/ação #1.pdf", false, default);
        Assert.Equal(runner.Text, result.ExtractedText);
        Assert.DoesNotContain("--invalidate-digital-signatures", runner.OcrArguments);
        Assert.Contains("--skip-text", runner.OcrArguments);
        Assert.Equal("-enc", runner.TextArguments[0]);
        Assert.Equal("UTF-8", runner.TextArguments[1]);
    }

    [Fact]
    public async Task Signed_document_failure_never_returns_a_derived_artifact()
    {
        var runner = new Runner { Signed = true };
        await Assert.ThrowsAsync<PdfHasDigitalSignatureException>(() => Service(runner).OcrizePdfAsync("assinado.pdf", false, default));
        Assert.Empty(runner.TextArguments);
    }

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 120)]
    [InlineData(3, 300)]
    [InlineData(4, 600)]
    [InlineData(5, 1200)]
    public void Retry_backoff_is_configurable_and_grows_exponentially(int attempt, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), new OcrOptions().RetryDelay(attempt));

    private static OcrMyPdfOcrService Service(Runner runner) => new(new Storage(), Options.Create(new OcrOptions()),
        new EnvironmentValidator(), NullLogger<OcrMyPdfOcrService>.Instance, runner);
    private sealed class EnvironmentValidator : IOcrEnvironmentValidator
    {
        public Task<OcrEnvironmentValidationResult> ValidateAsync(CancellationToken ct) => Task.FromResult(new OcrEnvironmentValidationResult { IsValid = true });
    }
    private sealed class Runner : IOcrProcessRunner
    {
        public int Exit;
        public bool Timeout;
        public bool Signed;
        public string Text = "texto";
        public string? FirstText;
        public int OcrCalls;
        public IReadOnlyList<string> OcrArguments = [];
        public IReadOnlyList<string> TextArguments = [];
        public async Task<OcrProcessResult> RunVersionAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory,
            IDictionary<string, string> environment, TimeSpan timeout, CancellationToken ct)
        {
            if (fileName.Contains("pdftotext"))
            {
                TextArguments = arguments;
                await File.WriteAllTextAsync(arguments[^1], OcrCalls == 1 ? FirstText ?? Text : Text, Encoding.UTF8, ct);
                return new(Exit, "segredo", "segredo", Timeout, 1, null);
            }
            OcrArguments = arguments;
            OcrCalls++;
            if (Signed) return new(6, "", "DigitalSignatureError", false, 1, null);
            await File.WriteAllBytesAsync(arguments[^1], "%PDF-synthetic"u8.ToArray(), ct);
            return new(0, "", "", false, 1, null);
        }
    }
    private sealed class Storage : IFileStorage
    {
        public Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream("%PDF-original"u8.ToArray()));
        public Task<(string, long, string, string)> SaveAsync(Stream content, string originalFileName, string contentType, Guid tenantId, Guid documentId, Guid versionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<(long, string, string)> SaveDerivedAsync(string path, Stream content, string contentType, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string path, CancellationToken ct) => Task.FromResult(true);
        public Task DeleteAsync(string path, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteIfExistsAsync(string path, CancellationToken ct) => throw new NotSupportedException();
    }
}
