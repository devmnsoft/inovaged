using System.Security.Cryptography;
using System.Text.Json;
using InovaGed.Application.Common.Preview;
using InovaGed.Application.Common.Storage;
using InovaGed.Infrastructure.Ocr;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InovaGed.Application.Tests;

public sealed class OcrEngineTheoryAttribute : TheoryAttribute
{
    public OcrEngineTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable("INOVAGED_OCR_FIXTURE_DIR")))
            Skip = "Teste opt-in de OCRmyPDF/Tesseract/Poppler com PDFs sintéticos gerados pelo script de homologação.";
    }
}

public sealed class OcrRealEngineTests
{
    [OcrEngineTheory]
    [InlineData("searchable")]
    [InlineData("scanned")]
    [InlineData("mixed")]
    [InlineData("tilted")]
    [InlineData("low-resolution")]
    [InlineData("table")]
    [InlineData("accents")]
    [InlineData("missing-fields")]
    [InlineData("contradictory")]
    public async Task Real_engine_returns_searchable_text_and_preserves_original_hash(string name)
    {
        var path = Path.Combine(System.Environment.GetEnvironmentVariable("INOVAGED_OCR_FIXTURE_DIR")!, name + ".pdf");
        var original = await File.ReadAllBytesAsync(path);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "InovaGed.sln"))) root = root.Parent;
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root!.FullName, "InovaGed.Web", "appsettings.json")));
        var settings = json.RootElement.GetProperty("Ocr").Deserialize<OcrOptions>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var options = Options.Create(settings);
        var storage = new ReadOnlyStorage();
        var runner = new OcrProcessRunner();
        var validator = new OcrEnvironmentValidator(options, Options.Create(new LibreOfficeOptions()), storage, runner);
        var service = new OcrMyPdfOcrService(storage, options, validator, NullLogger<OcrMyPdfOcrService>.Instance, runner);
        var result = await service.OcrizePdfAsync(path, false, default);
        Assert.Contains("Documento", result.ExtractedText, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.OcrPdfBytes.Length > 100);
        Assert.Equal(SHA256.HashData(original), SHA256.HashData(await File.ReadAllBytesAsync(path)));
        if (name == "mixed") Assert.Contains("002", result.ExtractedText);
        if (name == "accents") Assert.Contains("Conceição", result.ExtractedText, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ReadOnlyStorage : IFileStorage
    {
        public Task<Stream> OpenReadAsync(string path, CancellationToken ct) => Task.FromResult<Stream>(File.OpenRead(path));
        public Task<(string, long, string, string)> SaveAsync(Stream content, string name, string type, Guid tenantId, Guid documentId, Guid versionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<(long, string, string)> SaveDerivedAsync(string path, Stream content, string type, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string path, CancellationToken ct) => Task.FromResult(File.Exists(path));
        public Task DeleteAsync(string path, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteIfExistsAsync(string path, CancellationToken ct) => throw new NotSupportedException();
    }
}
