using InovaGed.Application.Signatures;

namespace InovaGed.Infrastructure.Signatures;

public sealed class NotConfiguredSignatureValidationService : ISignatureValidationService
{
    public Task<SignatureValidationReport> ValidateAsync(ValidateSignatureCommand command, CancellationToken ct) =>
        Task.FromResult(new SignatureValidationReport(Guid.NewGuid(), SignatureValidationStatus.NOT_VERIFIABLE, SignatureProfile.UNKNOWN, DateTimeOffset.UtcNow, "not-configured-icp-brasil-v1", new[] { new SignatureValidationCheck("DigitalSignature.Enabled", SignatureValidationStatus.NOT_VERIFIABLE, "Módulo ICP-Brasil real não habilitado/configurado; não declarar VALID.") }));
}

public sealed class NotConfiguredTimestampAuthorityClient : ITimestampAuthorityClient
{
    public Task<TimestampTokenResult> RequestTimestampAsync(byte[] hash, string hashAlgorithm, string? policyOid, CancellationToken ct) =>
        Task.FromResult(new TimestampTokenResult(false, null, null, SignatureValidationStatus.NOT_VERIFIABLE, "Autoridade de Carimbo do Tempo não configurada."));
}
