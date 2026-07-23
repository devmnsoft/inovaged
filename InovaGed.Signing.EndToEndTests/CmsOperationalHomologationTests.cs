using InovaGed.Application.Signatures;
using InovaGed.Infrastructure.Signatures;

namespace InovaGed.Signing.EndToEndTests;

public sealed class CmsOperationalHomologationTests
{
    [Fact]
    public void Completion_result_exposes_all_persisted_status_dimensions()
    {
        var result = new CompleteSignatureResult(true, Guid.NewGuid(), SignatureValidationStatus.VALID, SignatureValidationStatus.EXPIRED, SignatureValidationStatus.NOT_VERIFIABLE, SignatureValidationStatus.INVALID, SignatureConformityStatus.NOT_EVALUATED, null);

        Assert.True(result.Success);
        Assert.Equal(SignatureValidationStatus.VALID, result.CryptographicStatus);
        Assert.Equal(SignatureValidationStatus.EXPIRED, result.CertificateStatus);
        Assert.Equal(SignatureValidationStatus.NOT_VERIFIABLE, result.TrustStatus);
        Assert.Equal(SignatureConformityStatus.NOT_EVALUATED, result.ConformityStatus);
    }

    [Fact]
    public void Outcome_factory_preserves_untrusted_chain_status()
    {
        var factory = new SignatureValidationOutcomeFactory();
        var outcome = factory.Create(new[]
        {
            new SignatureValidationCheck("SIGNATURE_MATH", SignatureValidationStatus.VALID, "CMS válido."),
            new SignatureValidationCheck("MESSAGE_DIGEST", SignatureValidationStatus.VALID, "Conteúdo corresponde."),
            new SignatureValidationCheck("CERTIFICATE_VALIDITY", SignatureValidationStatus.VALID, "Certificado vigente."),
            new SignatureValidationCheck("CHAIN_BUILD", SignatureValidationStatus.UNTRUSTED_CHAIN, "Cadeia não confiável.")
        });

        Assert.Equal(SignatureValidationStatus.VALID, outcome.CryptographicStatus);
        Assert.Equal(SignatureValidationStatus.VALID, outcome.CertificateStatus);
        Assert.Equal(SignatureValidationStatus.UNTRUSTED_CHAIN, outcome.TrustStatus);
        Assert.Equal(SignatureValidationStatus.INDETERMINATE, outcome.ValidationStatus);
        Assert.Equal(SignatureConformityStatus.NOT_EVALUATED, outcome.ConformityStatus);
    }

    [Fact]
    public async Task Ten_parallel_requests_collapse_to_single_idempotent_signature_intent()
    {
        var unique = new HashSet<string>();
        var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(() =>
        {
            var command = new CompleteSigningSessionCommand(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                "completion-token",
                "idem-key",
                new byte[] { 1, 2, 3 },
                new byte[] { 4, 5, 6 },
                Array.Empty<byte[]>(),
                "operation-1",
                "agent-cms-detached-v1",
                "corr");
            return $"{command.TenantId:N}|{command.UserId:N}|{command.SessionId:N}|{command.IdempotencyKey}|{command.AgentOperationId}|{Convert.ToHexString(command.Cms)}";
        })).ToArray();

        foreach (var payload in await Task.WhenAll(tasks)) unique.Add(payload);
        Assert.Single(unique);
    }
}
