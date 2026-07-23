using System.IO;

namespace InovaGed.Application.Tests;

public sealed class SigningCmsReleaseCandidateTests
{
    [Fact]
    public void Orchestrator_contract_uses_typed_cms_commands()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var source = File.ReadAllText(Path.Combine(root, "InovaGed.Application", "Signatures", "DigitalSignatureContracts.cs"));
        Assert.Contains("Task<CreateSigningSessionResponse> PrepareAsync(PrepareSigningSessionCommand command", source);
        Assert.Contains("Task<CompleteSignatureResult> CompleteAsync(CompleteSigningSessionCommand command", source);
        Assert.Contains("[Obsolete", source);
    }
}
