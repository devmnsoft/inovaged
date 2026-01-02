namespace InovaGed.Application.Ocr;

public sealed class OcrResult
{
    public string Text { get; init; } = string.Empty;
    public string OutputPdfPath { get; init; } = default!; // PDF pesquisável gerado
    public string Language { get; init; } = "por";

    public int PageCount { get; init; }
}
