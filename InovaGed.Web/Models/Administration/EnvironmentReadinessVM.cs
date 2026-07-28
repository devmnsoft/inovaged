using InovaGed.Application.Readiness;
namespace InovaGed.Web.Models.Administration;
public sealed record EnvironmentReadinessVM(IReadOnlyList<ModuleReadinessResult> Modules, DateTimeOffset CheckedAtUtc) { public int Available => Modules.Count(item => item.Available); public int Pending => Modules.Count(item => !item.Available); }
