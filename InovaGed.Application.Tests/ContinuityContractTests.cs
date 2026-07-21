using InovaGed.Application.Continuity;

namespace InovaGed.Application.Tests;

public sealed class ContinuityContractTests
{
    [Fact]
    public void DefaultOptionsKeepExternalOperationsDisabled()
    {
        Assert.False(new BackupOptions().Enabled);
        Assert.False(new PortabilityOptions().Enabled);
        Assert.False(new OperationsOptions().WorkerEnabled);
    }

    [Fact]
    public void OffboardingDeletionExecutedIsNotAutomaticInContract()
    {
        var dto = new TenantOffboardingDto(Guid.NewGuid(), Guid.NewGuid(), "LEGAL_HOLD", DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow.AddDays(90), true, "retenção legal");
        Assert.True(dto.LegalHold);
        Assert.Equal("LEGAL_HOLD", dto.Status);
    }

    [Fact]
    public void PortabilityManifestUsesOpenFormatVersion()
    {
        var manifest = new PortabilityManifest("1.0", Guid.NewGuid(), Guid.NewGuid(), "TENANT", DateTime.UtcNow, "PENDING", "corr", []);
        Assert.Equal("1.0", manifest.FormatVersion);
        Assert.Empty(manifest.Files);
    }
}
