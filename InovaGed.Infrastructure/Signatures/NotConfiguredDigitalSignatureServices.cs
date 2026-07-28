using InovaGed.Application.Signatures;

namespace InovaGed.Infrastructure.Signatures;

public sealed class NotConfiguredSignatureValidationService : ISignatureValidationService
{
    private const string EngineVersion = "not-configured-digital-signature-v2";

    public Task<SignatureValidationReport> ValidateAsync(ValidateSignatureCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ct.ThrowIfCancellationRequested();

        return Task.FromResult(CreateReport(
            "DIGITAL_SIGNATURE_DISABLED",
            "O módulo de assinatura digital não está habilitado ou configurado. A assinatura não foi validada."));
    }

    public Task<SignatureValidationReport> ValidateDetachedAsync(
        Stream content,
        ReadOnlyMemory<byte> cms,
        ReadOnlyMemory<byte>? expectedCertificate,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        ct.ThrowIfCancellationRequested();

        // Deliberately do not inspect any of the inputs. This fallback must not perform,
        // or give the impression that it performed, cryptographic validation.
        return Task.FromResult(CreateReport(
            "CMS_VALIDATION_NOT_CONFIGURED",
            "A validação CMS destacada não está habilitada ou configurada. Não foi realizada validação criptográfica."));
    }

    private static SignatureValidationReport CreateReport(string checkName, string message)
    {
        IReadOnlyList<SignatureValidationCheck> checks =
        [
            new(checkName, SignatureValidationStatus.NOT_VERIFIABLE, message)
        ];

        return new SignatureValidationReport(
            Guid.NewGuid(),
            SignatureValidationStatus.NOT_VERIFIABLE,
            SignatureProfile.UNKNOWN,
            DateTimeOffset.UtcNow,
            EngineVersion,
            checks);
    }
}

public sealed class NotConfiguredTimestampAuthorityClient : ITimestampAuthorityClient
{
    public Task<TimestampTokenResult> RequestTimestampAsync(byte[] hash, string hashAlgorithm, string? policyOid, CancellationToken ct) =>
        Task.FromResult(new TimestampTokenResult(false, null, null, SignatureValidationStatus.NOT_VERIFIABLE, "Autoridade de Carimbo do Tempo não configurada."));
}


public sealed class NotConfiguredSigningOrchestrator : ISigningOrchestrator
{
    private static SignatureValidationReport NotVerifiableReport(string message) => new(
        Guid.NewGuid(),
        SignatureValidationStatus.NOT_VERIFIABLE,
        SignatureProfile.UNKNOWN,
        DateTimeOffset.UtcNow,
        "not-configured-icp-brasil-v1",
        new[] { new SignatureValidationCheck("DigitalSignature.Enabled", SignatureValidationStatus.NOT_VERIFIABLE, message) });

    public Task<CreateSigningSessionResponse> PrepareAsync(PrepareSigningSessionCommand command, CancellationToken ct) =>
        throw new InvalidOperationException("Módulo de assinatura digital desabilitado.");

    public Task<CompleteSignatureResult> CompleteAsync(CompleteSigningSessionCommand command, CancellationToken ct) =>
        Task.FromResult(new CompleteSignatureResult(false, null, SignatureValidationStatus.NOT_VERIFIABLE, "Módulo de assinatura digital desabilitado."));

    public Task<SignatureValidationReport> ValidateAsync(ValidateSignatureCommand command, CancellationToken ct) =>
        Task.FromResult(NotVerifiableReport("Módulo de assinatura digital desabilitado; resultado criptográfico não verificável."));
}
