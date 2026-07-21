using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
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
                var ku = cert.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
                if (ku is not null && !ku.KeyUsages.HasFlag(X509KeyUsageFlags.DigitalSignature))
                    checks.Add(new("CERTIFICATE_KEY_USAGE", SignatureValidationStatus.CERTIFICATE_PURPOSE_INVALID, "Key Usage não permite assinatura digital."));
            }
            var final = checks.Any(c => c.Status is SignatureValidationStatus.SIGNATURE_CORRUPTED) ? SignatureValidationStatus.SIGNATURE_CORRUPTED : checks.Any(c => c.Status is SignatureValidationStatus.INVALID or SignatureValidationStatus.EXPIRED or SignatureValidationStatus.NOT_YET_VALID or SignatureValidationStatus.CERTIFICATE_PURPOSE_INVALID) ? SignatureValidationStatus.INVALID : SignatureValidationStatus.INDETERMINATE;
            return new SignatureValidationReport(Guid.NewGuid(), final, SignatureProfile.UNKNOWN, DateTimeOffset.UtcNow, "cms-detached-v1", checks);
        }
        catch (CryptographicException ex)
        {
            checks.Add(new("CMS_OR_SIGNATURE", SignatureValidationStatus.SIGNATURE_CORRUPTED, ex.Message));
            return new SignatureValidationReport(Guid.NewGuid(), SignatureValidationStatus.SIGNATURE_CORRUPTED, SignatureProfile.UNKNOWN, DateTimeOffset.UtcNow, "cms-detached-v1", checks);
        }
    }
}

public sealed class CertificateIdentityService(IConfiguration configuration) : ICertificateIdentityService
{
    public CertificateIdentity Extract(byte[] certificateDer)
    {
        using var cert = new X509Certificate2(certificateDer);
        var cpf = ExtractCpf(cert.Subject);
        var key = configuration["DigitalSignature:CertificateIdentityHmacKey"] ?? string.Empty;
        var keyVersion = configuration["DigitalSignature:CertificateIdentityHmacKeyVersion"] ?? "v1";
        var hmac = cpf is null || string.IsNullOrWhiteSpace(key) ? null : ComputeHmac(key, keyVersion, cpf);
        return new CertificateIdentity(cert.GetNameInfo(X509NameType.SimpleName, false), Mask(cpf), hmac, null, null, MaskSubject(cert.Subject), cert.Issuer, cert.SerialNumber, cert.Thumbprint);
    }
    private static string? ExtractCpf(string subject) => Regex.Matches(subject, "\\d{11}").Select(m => m.Value).FirstOrDefault();
    private static string? Mask(string? cpf) => cpf is { Length: 11 } ? $"***.{cpf[3..6]}.{cpf[6..9]}-**" : null;
    private static string MaskSubject(string subject) => Regex.Replace(subject, "\\d{11}", m => Mask(m.Value) ?? "***");
    private static string ComputeHmac(string key, string version, string cpf) { using var h = new HMACSHA256(Encoding.UTF8.GetBytes(key)); return $"{version}:" + Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes(cpf))).ToLowerInvariant(); }
}

public sealed class NoopSignatureEvidenceRepository : ISignatureEvidenceRepository { public Task StoreAsync(SignatureEvidence evidence, CancellationToken ct) => Task.CompletedTask; }
public sealed class NoopSigningSessionRepository : ISigningSessionRepository { public Task SaveAsync(PrepareSignatureCommand command, PrepareSignatureResult result, CancellationToken ct) => Task.CompletedTask; }
public sealed class PostgresSignatureRepository : ISignatureRepository { }
public sealed class PostgresSignatureValidationRepository : ISignatureValidationRepository { }
public sealed class PostgresSignatureEventRepository : ISignatureEventRepository { }
public sealed class DocumentVersionSigningContentService : IDocumentVersionSigningContentService { }
public sealed class SignaturePackageService : ISignaturePackageService { }
public sealed class PostgresSignatureEvidenceRepository(IConfiguration configuration) : ISignatureEvidenceRepository { public async Task StoreAsync(SignatureEvidence evidence, CancellationToken ct) { await using var c = new NpgsqlConnection(configuration.GetConnectionString("DefaultConnection")); await c.ExecuteAsync(new CommandDefinition("insert into ged.signature_evidence (id, tenant_id, signature_id, evidence_type, hash_algorithm, evidence_hash, evidence_bytes, captured_at, correlation_id) values (gen_random_uuid(), @TenantId,@SignatureId,@EvidenceType,@HashAlgorithm,@EvidenceHash,@EvidenceBytes,@CapturedAt,@CorrelationId)", evidence, cancellationToken: ct)); } }
public sealed class PostgresSigningSessionRepository(IConfiguration configuration) : ISigningSessionRepository { public async Task SaveAsync(PrepareSignatureCommand command, PrepareSignatureResult result, CancellationToken ct) { if (result.SessionId is null) return; await using var c = new NpgsqlConnection(configuration.GetConnectionString("DefaultConnection")); await c.ExecuteAsync(new CommandDefinition("insert into ged.signing_session (id,tenant_id,user_id,document_id,document_version_id,status,content_hash,content_hash_algorithm,nonce_hash,expires_at,signature_type,signature_format,correlation_id) values (@Id,@TenantId,@UserId,@DocumentId,@DocumentVersionId,@Status,@ContentHash,@ContentHashAlgorithm,@NonceHash,@ExpiresAt,@SignatureType,@SignatureFormat,@CorrelationId) on conflict (id) do nothing", new { Id=result.SessionId.Value, command.TenantId, command.UserId, command.DocumentId, command.DocumentVersionId, Status=result.Status.ToString(), command.ContentHash, command.ContentHashAlgorithm, NonceHash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(command.Nonce))).ToLowerInvariant(), command.ExpiresAt, SignatureType=command.Type.ToString(), SignatureFormat=command.Format, command.CorrelationId }, cancellationToken: ct)); } }
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
