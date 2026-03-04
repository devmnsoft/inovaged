namespace InovaGed.Application.Ged.Instruments
{
    public interface IClassificationPlanQueries
    {
        Task<IReadOnlyList<ClassificationPlanRow>> ListAsync(Guid tenantId, CancellationToken ct);

        Task<IReadOnlyList<ClassificationPlanVersionRow>> ListVersionsAsync(Guid tenantId, CancellationToken ct);

        Task<string> ExportCurrentCsvAsync(Guid tenantId, Guid? rootId, CancellationToken ct);
        Task<string> ExportVersionCsvAsync(Guid tenantId, Guid versionId, CancellationToken ct);
    }

    public sealed class ClassificationPlanVersionRow
    {
        public Guid Id { get; init; }
        public int VersionNo { get; init; }
        public string Title { get; init; } = "";
        public string? Notes { get; init; }
        public DateTimeOffset PublishedAt { get; init; }
        public Guid? PublishedBy { get; init; }
    }
}
