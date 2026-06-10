namespace InovaGed.Application.Ocr;

public interface IOcrAutoSchedulerService
{
    Task<OcrAutoScheduleRunResultDto> RunAsync(CancellationToken ct);
}

public interface IOcrAutoScheduleRepository
{
    Task<IReadOnlyList<OcrAutoScheduleCandidateDto>> GetDocumentsWithoutOcrAsync(OcrAutoScheduleOptions options, CancellationToken ct);
    Task<int> CountDocumentsWithoutOcrAsync(OcrAutoScheduleOptions options, CancellationToken ct);
    Task<string?> GetLatestOcrJobStatusAsync(Guid tenantId, Guid versionId, CancellationToken ct);
    Task<bool> HasOcrAvailableAsync(Guid tenantId, Guid versionId, CancellationToken ct);
    Task<Guid> InsertRunAsync(OcrAutoScheduleRunResultDto result, CancellationToken ct);
    Task UpdateRunAsync(OcrAutoScheduleRunResultDto result, CancellationToken ct);
    Task InsertRunItemAsync(Guid runId, Guid tenantId, OcrAutoScheduleItemResultDto item, CancellationToken ct);
    Task<IReadOnlyList<OcrAutoScheduleRunSummaryDto>> GetRunHistoryAsync(Guid tenantId, int take, CancellationToken ct);
    Task<OcrAutoScheduleRunSummaryDto?> GetLastRunAsync(Guid tenantId, CancellationToken ct);
}
