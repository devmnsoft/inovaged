namespace InovaGed.Application.Readiness;
public sealed record ModuleReadinessResult(string ModuleCode, bool Enabled, bool SchemaReady, bool DependenciesReady, bool Available, string Status, IReadOnlyList<string> MissingObjects, IReadOnlyList<string> Recommendations, DateTimeOffset CheckedAtUtc);
public interface IModuleReadinessService { Task<ModuleReadinessResult> GetAsync(string moduleCode, CancellationToken cancellationToken = default); }
