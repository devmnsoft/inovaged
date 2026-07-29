using InovaGed.Application.Readiness;
namespace InovaGed.Web.Models.Administration;
public sealed record EnvironmentReadinessVM(IReadOnlyList<ModuleReadinessResult> Modules, DateTimeOffset CheckedAtUtc, ReleaseReadinessVM Release) { public int Available => Modules.Count(item => item.Available); public int Pending => Modules.Count(item => !item.Available); }
public sealed record ReleaseReadinessVM(string Version, string Commit, string DeploymentId, string PreviousRelease, string Schema, DateTimeOffset? DeployedAtUtc, bool RollbackAvailable);
