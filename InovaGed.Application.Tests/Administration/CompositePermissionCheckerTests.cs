using InovaGed.Application.Administration;
using Xunit;

namespace InovaGed.Application.Tests.Administration;

public class CompositePermissionCheckerTests
{
    [Fact] public async Task LegacyKeepsAllowAllCompatibility(){ var repo=new Repo(PermissionMode.LEGACY); var checker=new CompositePermissionChecker(new Real(false), repo); Assert.True(await checker.IsAllowedAsync(Guid.NewGuid(),Guid.NewGuid(),"GED.View")); Assert.Equal(0, repo.Logs); }
    [Fact] public async Task AuditOnlyDoesNotBlockButLogsDivergence(){ var repo=new Repo(PermissionMode.AUDIT_ONLY); var checker=new CompositePermissionChecker(new Real(false), repo); Assert.True(await checker.IsAllowedAsync(Guid.NewGuid(),Guid.NewGuid(),"GED.View")); Assert.Equal(1, repo.Logs); }
    [Fact] public async Task EnforcedBlocksWhenRealPermissionDenies(){ var repo=new Repo(PermissionMode.ENFORCED); var checker=new CompositePermissionChecker(new Real(false), repo); Assert.False(await checker.IsAllowedAsync(Guid.NewGuid(),Guid.NewGuid(),"GED.View")); Assert.Equal(1, repo.Logs); }
    private sealed class Real(bool allowed) : IRealPermissionChecker { public Task<bool> IsAllowedAsync(Guid tenantId, Guid userId, string permissionCode, CancellationToken ct = default) => Task.FromResult(allowed); }
    private sealed class Repo(PermissionMode mode) : IPermissionGovernanceRepository { public int Logs { get; private set; } public Task<PermissionMode> GetModeAsync(Guid tenantId, CancellationToken ct = default)=>Task.FromResult(mode); public Task LogEvaluationAsync(PermissionEvaluationContext context, bool legacyAllowed, bool realAllowed, PermissionMode mode, CancellationToken ct = default){ Logs++; return Task.CompletedTask; } }
}
