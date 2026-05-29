namespace InovaGed.Application.HospitalAnalytics;

public interface IHospitalOcrAnalyticsService
{
    Task<HospitalOcrAnalyticsSnapshotDto> BuildSnapshotAsync(HospitalOcrAnalyticsFilter filter, CancellationToken ct);

    Task<IReadOnlyList<TermMatchDto>> AnalyzeTermsAsync(
        HospitalOcrAnalyticsSnapshotDto snapshot,
        IReadOnlyList<TermDictionaryItemDto> dictionary,
        CancellationToken ct);

    Task<IReadOnlyList<MoneySignalDto>> AnalyzeMoneySignalsAsync(
        HospitalOcrAnalyticsSnapshotDto snapshot,
        CancellationToken ct);
}
