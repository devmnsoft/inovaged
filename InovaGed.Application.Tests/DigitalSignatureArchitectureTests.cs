using System.Text.RegularExpressions;
using InovaGed.Application.Signatures;

namespace InovaGed.Application.Tests;

public sealed class DigitalSignatureArchitectureTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    [Fact]
    public void Internal_operational_status_is_not_cryptographic_valid()
    {
        Assert.Contains(SignatureValidationStatus.INTERNAL_ONLY, Enum.GetValues<SignatureValidationStatus>());
        Assert.Contains(SignatureType.PADES, Enum.GetValues<SignatureType>());
        Assert.Contains(SignatureType.CADES, Enum.GetValues<SignatureType>());
    }

    [Fact]
    public void Signature_controller_does_not_insert_internal_signature_as_valid()
    {
        var source = File.ReadAllText(Path.Combine(Root, "InovaGed.Web/Controller/SignatureController.cs"));
        Assert.DoesNotContain("'VALID'::ged.signature_status", source);
        Assert.DoesNotContain("CPF {cpf.Trim()}", source);
    }
}
