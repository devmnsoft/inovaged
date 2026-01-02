namespace InovaGed.Application.Ocr;

public interface IOcrProcessor
{
    Task<OcrResult> ProcessAsync(OcrJobDto job, CancellationToken ct);
}
 