namespace InovaGed.Environment.Doctor.Quality;

public enum QualityStatus { Pass, Warning, Fail }
public sealed record QualityFinding(string Check, QualityStatus Status, string Message, string? Impact = null, string? Action = null, string? Script = null, string? Resource = null);
public sealed class QualityGateReport
{
    public string Name { get; init; } = "InovaGED Quality Gate";
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Environment { get; init; } = System.Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Not configured";
    public string ConnectionString { get; init; } = "Not configured";
    public List<QualityFinding> Checks { get; init; } = [];
    public QualityStatus Status => Checks.Any(x => x.Status == QualityStatus.Fail) ? QualityStatus.Fail : Checks.Any(x => x.Status == QualityStatus.Warning) ? QualityStatus.Warning : QualityStatus.Pass;
}
public sealed record QualityContext(string Root, bool NoDatabaseRequired, string? ConnectionString, string? BaseUrl);
public interface IQualityCheck { string Name { get; } Task<IReadOnlyList<QualityFinding>> RunAsync(QualityContext context, CancellationToken cancellationToken); }
