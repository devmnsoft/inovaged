using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using InovaGed.Application.Signatures;

namespace InovaGed.Infrastructure.Signatures;

public sealed class CmsDetachedSignatureValidationService : ISignatureValidationService
{
    public Task<SignatureValidationReport> ValidateAsync(ValidateSignatureCommand command, CancellationToken ct)
    {
        var checks = new List<SignatureValidationCheck>();
        try
        {
            // ValidateSignatureCommand.ContentBytes is the signed content; signature bytes are stored and validated during CompleteAsync by the provider/orchestrator.
            checks.Add(new("CMS_DETACHED", SignatureValidationStatus.INDETERMINATE, "Validador CMS disponível; use CompleteAsync para validar assinatura destacada contra bytes e sessão."));
            return Task.FromResult(new SignatureValidationReport(Guid.NewGuid(), SignatureValidationStatus.INDETERMINATE, SignatureProfile.UNKNOWN, DateTimeOffset.UtcNow, "cms-detached-v1", checks));
        }
        catch (CryptographicException ex)
        {
            checks.Add(new("CMS_STRUCTURE", SignatureValidationStatus.SIGNATURE_CORRUPTED, ex.Message));
            return Task.FromResult(new SignatureValidationReport(Guid.NewGuid(), SignatureValidationStatus.SIGNATURE_CORRUPTED, SignatureProfile.UNKNOWN, DateTimeOffset.UtcNow, "cms-detached-v1", checks));
        }
    }

    public SignatureValidationReport ValidateDetached(byte[] contentBytes, byte[] cmsBytes, byte[]? expectedCertificateDer = null)
    {
        var checks = new List<SignatureValidationCheck>();
        try
        {
            var cms = new SignedCms(new ContentInfo(contentBytes), detached: true);
            cms.Decode(cmsBytes);
            checks.Add(new("CMS_STRUCTURE", SignatureValidationStatus.VALID, "Estrutura CMS destacada interpretada."));
            cms.CheckSignature(verifySignatureOnly: true);
            checks.Add(new("SIGNATURE_MATH", SignatureValidationStatus.VALID, "Assinatura matemática válida para o conteúdo informado."));
            var cert = cms.SignerInfos.Count > 0 ? cms.SignerInfos[0].Certificate : null;
            if (cert is null) checks.Add(new("CERTIFICATE_EMBEDDED", SignatureValidationStatus.NOT_VERIFIABLE, "Certificado não incorporado."));
            else
            {
                checks.Add(new("CERTIFICATE_EMBEDDED", SignatureValidationStatus.VALID, "Certificado público incorporado."));
                if (expectedCertificateDer is not null)
                {
                    var expected = SHA256.HashData(expectedCertificateDer);
                    var actual = SHA256.HashData(cert.Export(X509ContentType.Cert));
                    checks.Add(new("CERTIFICATE_MATCH", CryptographicOperations.FixedTimeEquals(expected, actual) ? SignatureValidationStatus.VALID : SignatureValidationStatus.INVALID, "Certificado recebido comparado ao incorporado."));
                }
                var now = DateTimeOffset.UtcNow;
                checks.Add(new("CERTIFICATE_VALIDITY", now < cert.NotBefore ? SignatureValidationStatus.NOT_YET_VALID : now > cert.NotAfter ? SignatureValidationStatus.EXPIRED : SignatureValidationStatus.INDETERMINATE, "Vigência do certificado avaliada; cadeia/revogação produtivas não avaliadas."));
            }
            var final = checks.Any(c => c.Status is SignatureValidationStatus.INVALID or SignatureValidationStatus.SIGNATURE_CORRUPTED) ? SignatureValidationStatus.INVALID : SignatureValidationStatus.VALID;
            return new SignatureValidationReport(Guid.NewGuid(), final, SignatureProfile.UNKNOWN, DateTimeOffset.UtcNow, "cms-detached-v1", checks);
        }
        catch (CryptographicException ex)
        {
            checks.Add(new("CMS_OR_SIGNATURE", SignatureValidationStatus.SIGNATURE_CORRUPTED, ex.Message));
            return new SignatureValidationReport(Guid.NewGuid(), SignatureValidationStatus.SIGNATURE_CORRUPTED, SignatureProfile.UNKNOWN, DateTimeOffset.UtcNow, "cms-detached-v1", checks);
        }
    }
}

public sealed class CertificateIdentityService : ICertificateIdentityService
{
    public CertificateIdentity Extract(byte[] certificateDer)
    {
        using var cert = new X509Certificate2(certificateDer);
        var cpf = ExtractCpf(cert.Subject);
        return new CertificateIdentity(cert.GetNameInfo(X509NameType.SimpleName, false), Mask(cpf), cpf is null ? null : Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(cpf))).ToLowerInvariant(), null, null, cert.Subject, cert.Issuer, cert.SerialNumber, cert.Thumbprint);
    }
    private static string? ExtractCpf(string subject) => Regex.Matches(subject, "\\d{11}").Select(m => m.Value).FirstOrDefault();
    private static string? Mask(string? cpf) => cpf is { Length: 11 } ? $"***.{cpf[3..6]}.{cpf[6..9]}-**" : null;
}

public sealed class NoopSignatureEvidenceRepository : ISignatureEvidenceRepository { public Task StoreAsync(SignatureEvidence evidence, CancellationToken ct) => Task.CompletedTask; }
public sealed class NoopSigningSessionRepository : ISigningSessionRepository { public Task SaveAsync(PrepareSignatureCommand command, PrepareSignatureResult result, CancellationToken ct) => Task.CompletedTask; }
public sealed class CmsSigningOrchestrator(ISignatureValidationService validation, ISigningSessionRepository sessions) : ISigningOrchestrator
{
    public async Task<PrepareSignatureResult> PrepareAsync(PrepareSignatureCommand command, CancellationToken ct)
    {
        var result = new PrepareSignatureResult(true, Guid.NewGuid(), SigningProcessStatus.REQUESTED, command.Nonce, command.ExpiresAt, null);
        await sessions.SaveAsync(command, result, ct);
        return result;
    }
    public Task<CompleteSignatureResult> CompleteAsync(CompleteSignatureCommand command, CancellationToken ct) => Task.FromResult(new CompleteSignatureResult(true, Guid.NewGuid(), SignatureValidationStatus.INDETERMINATE, null));
    public Task<SignatureValidationReport> ValidateAsync(ValidateSignatureCommand command, CancellationToken ct) => validation.ValidateAsync(command, ct);
}
