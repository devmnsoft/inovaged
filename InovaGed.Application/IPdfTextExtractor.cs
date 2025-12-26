namespace InovaGed.Application
{
    public interface IPdfTextExtractor
    {
        Task<string> ExtractTextAsync(string pdfStoragePath, CancellationToken ct);
    }
}
