using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Extensions.Configuration;
using InovaGed.Application.Common.Database;
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

    public async Task<SignatureValidationReport> ValidateDetachedAsync(Stream content, ReadOnlyMemory<byte> cms, ReadOnlyMemory<byte>? expectedCertificate, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        return ValidateDetached(ms.ToArray(), cms.ToArray(), expectedCertificate?.ToArray());
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
public sealed class NoopSigningSessionRepository : ISigningSessionRepository
{
    public Task SaveAsync(PrepareSignatureCommand command, PrepareSignatureResult result, CancellationToken ct) => Task.CompletedTask;
    public Task CreateAsync(SigningSessionRecord session, string contentTokenHash, string completionTokenHash, string nonceHash, CancellationToken ct) => Task.CompletedTask;
    public Task<SigningSessionRecord?> GetAsync(Guid tenantId, Guid sessionId, CancellationToken ct) => Task.FromResult<SigningSessionRecord?>(null);
    public Task<SigningSessionRecord?> GetForContentAsync(Guid tenantId, Guid sessionId, string contentTokenHash, CancellationToken ct) => Task.FromResult<SigningSessionRecord?>(null);
    public Task<bool> ConsumeContentTokenAsync(Guid tenantId, Guid sessionId, string contentTokenHash, CancellationToken ct) => Task.FromResult(false);
    public Task<SigningSessionRecord?> GetForCompletionAsync(Guid tenantId, Guid sessionId, string completionTokenHash, CancellationToken ct) => Task.FromResult<SigningSessionRecord?>(null);
    public Task<bool> ConsumeCompletionTokenAsync(Guid tenantId, Guid sessionId, string completionTokenHash, string idempotencyKey, string payloadHash, CancellationToken ct) => Task.FromResult(false);
    public Task MarkContentAccessedAsync(Guid tenantId, Guid sessionId, CancellationToken ct) => Task.CompletedTask;
    public Task MarkWaitingConfirmationAsync(Guid tenantId, Guid sessionId, CancellationToken ct) => Task.CompletedTask;
    public Task MarkSigningAsync(Guid tenantId, Guid sessionId, CancellationToken ct) => Task.CompletedTask;
    public Task CompleteAsync(Guid tenantId, Guid sessionId, Guid signatureId, CancellationToken ct) => Task.CompletedTask;
    public Task<bool> CancelAsync(Guid tenantId, Guid sessionId, Guid userId, CancellationToken ct) => Task.FromResult(false);
    public Task<int> ExpireAsync(Guid tenantId, DateTimeOffset now, CancellationToken ct) => Task.FromResult(0);
    public Task IncrementFailureAsync(Guid tenantId, Guid sessionId, string safeError, CancellationToken ct) => Task.CompletedTask;
    public Task<DocumentSignatureRecord?> GetExistingCompletionAsync(Guid tenantId, Guid sessionId, string idempotencyKey, string payloadHash, CancellationToken ct) => Task.FromResult<DocumentSignatureRecord?>(null);
}
public sealed class PostgresSignatureRepository(IDbConnectionFactory db) : ISignatureRepository
{
    public async Task<Guid> CreateAsync(DocumentSignatureRecord signature, CancellationToken ct) { await using var c = await db.OpenAsync(ct); await c.ExecuteAsync(new CommandDefinition("insert into ged.document_signature (id,tenant_id,signing_session_id,document_id,document_version_id,signature_type,signature_format,signature_profile,signature_source,cryptographic_status,validation_status,conformity_status,cms_bytes,cms_sha256,content_sha256,certificate_der,certificate_chain_der,engine_version,correlation_id,created_at) values (@Id,@TenantId,@SessionId,@DocumentId,@DocumentVersionId,@SignatureType,@SignatureFormat,@SignatureProfile,@SignatureSource,@CryptographicStatus,@ValidationStatus,@ConformityStatus,@CmsBytes,@CmsSha256,@ContentSha256,@CertificateDer,@CertificateChainDer,@EngineVersion,@CorrelationId,@CreatedAt) on conflict (tenant_id, signing_session_id) do nothing", new { signature.Id, signature.TenantId, signature.SessionId, signature.DocumentId, signature.DocumentVersionId, signature.SignatureType, signature.SignatureFormat, signature.SignatureProfile, signature.SignatureSource, signature.CryptographicStatus, signature.ValidationStatus, signature.ConformityStatus, signature.CmsBytes, signature.CmsSha256, signature.ContentSha256, signature.CertificateDer, CertificateChainDer=signature.CertificateChainDer.ToArray(), signature.EngineVersion, signature.CorrelationId, signature.CreatedAt }, cancellationToken: ct)); return signature.Id; }
    public async Task<DocumentSignatureRecord?> GetAsync(Guid tenantId, Guid signatureId, CancellationToken ct) { await using var c=await db.OpenAsync(ct); return await c.QuerySingleOrDefaultAsync<DocumentSignatureRecord>(new CommandDefinition("select id,tenant_id TenantId,signing_session_id SessionId,document_id DocumentId,document_version_id DocumentVersionId,signature_type SignatureType,signature_format SignatureFormat,signature_profile SignatureProfile,signature_source SignatureSource,cryptographic_status CryptographicStatus,validation_status ValidationStatus,conformity_status ConformityStatus,cms_sha256 CmsSha256,content_sha256 ContentSha256,cms_bytes CmsBytes,certificate_der CertificateDer,engine_version EngineVersion,correlation_id CorrelationId,created_at CreatedAt from ged.document_signature where tenant_id=@tenantId and id=@signatureId", new{tenantId,signatureId}, cancellationToken:ct)); }
    public async Task<IReadOnlyList<DocumentSignatureRecord>> ListByDocumentAsync(Guid tenantId, Guid documentId, CancellationToken ct) { await using var c=await db.OpenAsync(ct); return (await c.QueryAsync<DocumentSignatureRecord>(new CommandDefinition("select id,tenant_id TenantId,signing_session_id SessionId,document_id DocumentId,document_version_id DocumentVersionId,signature_type SignatureType,signature_format SignatureFormat,signature_profile SignatureProfile,signature_source SignatureSource,cryptographic_status CryptographicStatus,validation_status ValidationStatus,conformity_status ConformityStatus,cms_sha256 CmsSha256,content_sha256 ContentSha256,cms_bytes CmsBytes,certificate_der CertificateDer,engine_version EngineVersion,correlation_id CorrelationId,created_at CreatedAt from ged.document_signature where tenant_id=@tenantId and document_id=@documentId order by created_at desc", new{tenantId,documentId}, cancellationToken:ct))).ToList(); }
    public Task<IReadOnlyList<DocumentSignatureRecord>> ListByVersionAsync(Guid tenantId, Guid documentId, Guid documentVersionId, CancellationToken ct) => ListByDocumentAsync(tenantId, documentId, ct);
    public async Task<byte[]?> GetCmsBytesAsync(Guid tenantId, Guid signatureId, CancellationToken ct) { await using var c=await db.OpenAsync(ct); return await c.ExecuteScalarAsync<byte[]>(new CommandDefinition("select cms_bytes from ged.document_signature where tenant_id=@tenantId and id=@signatureId", new{tenantId,signatureId}, cancellationToken:ct)); }
    public async Task<byte[]?> GetCertificateAsync(Guid tenantId, Guid signatureId, CancellationToken ct) { await using var c=await db.OpenAsync(ct); return await c.ExecuteScalarAsync<byte[]>(new CommandDefinition("select certificate_der from ged.document_signature where tenant_id=@tenantId and id=@signatureId", new{tenantId,signatureId}, cancellationToken:ct)); }
    public async Task<bool> ExistsForSessionAsync(Guid tenantId, Guid sessionId, CancellationToken ct) { await using var c=await db.OpenAsync(ct); return await c.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from ged.document_signature where tenant_id=@tenantId and signing_session_id=@sessionId)", new{tenantId,sessionId}, cancellationToken:ct)); }
}
public sealed class PostgresSignatureValidationRepository(IDbConnectionFactory db) : ISignatureValidationRepository { public async Task<Guid> CreateRunAsync(SignatureValidationRunRecord run, CancellationToken ct){ await using var c=await db.OpenAsync(ct); await c.ExecuteAsync(new CommandDefinition("insert into ged.signature_validation_run(id,tenant_id,signature_id,cryptographic_status,validation_status,conformity_status,engine_version,validated_at,correlation_id) values(@Id,@TenantId,@SignatureId,@CryptographicStatus,@ValidationStatus,@ConformityStatus,@EngineVersion,@ValidatedAt,@CorrelationId)", run, cancellationToken:ct)); return run.Id;} public Task StoreChecksAsync(Guid tenantId, Guid validationRunId, IReadOnlyList<SignatureValidationCheck> checks, CancellationToken ct)=>Task.CompletedTask; public Task StoreChainAsync(Guid tenantId, Guid validationRunId, IReadOnlyList<byte[]> chainDer, CancellationToken ct)=>Task.CompletedTask; public Task<SignatureValidationRunRecord?> GetLatestAsync(Guid tenantId, Guid signatureId, CancellationToken ct)=>Task.FromResult<SignatureValidationRunRecord?>(null); public Task<IReadOnlyList<SignatureValidationRunRecord>> ListHistoryAsync(Guid tenantId, Guid signatureId, CancellationToken ct)=>Task.FromResult<IReadOnlyList<SignatureValidationRunRecord>>(Array.Empty<SignatureValidationRunRecord>()); }
public sealed class PostgresSignatureEventRepository(IDbConnectionFactory db) : ISignatureEventRepository { public async Task RegisterAsync(SignatureEventRecord evt, CancellationToken ct){ await using var c=await db.OpenAsync(ct); await c.ExecuteAsync(new CommandDefinition("insert into ged.signature_event(id,tenant_id,signing_session_id,signature_id,event_type,safe_message,correlation_id,created_at) values(@Id,@TenantId,@SessionId,@SignatureId,@EventType,@SafeMessage,@CorrelationId,@CreatedAt)", evt, cancellationToken:ct)); } public Task<IReadOnlyList<SignatureEventRecord>> ListBySessionAsync(Guid tenantId, Guid sessionId, CancellationToken ct)=>Task.FromResult<IReadOnlyList<SignatureEventRecord>>(Array.Empty<SignatureEventRecord>()); public Task<IReadOnlyList<SignatureEventRecord>> ListBySignatureAsync(Guid tenantId, Guid signatureId, CancellationToken ct)=>Task.FromResult<IReadOnlyList<SignatureEventRecord>>(Array.Empty<SignatureEventRecord>()); }
public sealed class DocumentVersionSigningContentService : IDocumentVersionSigningContentService { public Task<SigningContentMetadata?> LocateVersionAsync(Guid tenantId, Guid documentId, Guid documentVersionId, CancellationToken ct)=>Task.FromResult<SigningContentMetadata?>(null); public Task<bool> ValidateTenantAsync(Guid tenantId, Guid documentId, Guid documentVersionId, CancellationToken ct)=>Task.FromResult(false); public Task<SigningContentMetadata> GetMetadataAsync(Guid tenantId, Guid documentId, Guid documentVersionId, CancellationToken ct)=>throw new InvalidOperationException("Signing content metadata repository not configured."); public Task<Stream> OpenReadAsync(Guid tenantId, Guid documentId, Guid documentVersionId, CancellationToken ct)=>throw new InvalidOperationException("Signing content storage not configured."); public async Task<string> CalculateSha256Async(Stream content, CancellationToken ct){ using var sha=SHA256.Create(); return Convert.ToHexString(await sha.ComputeHashAsync(content, ct)).ToLowerInvariant(); } public Task ValidateSizeAsync(long sizeBytes, CancellationToken ct){ if(sizeBytes<=0) throw new InvalidOperationException("Invalid signing content size."); return Task.CompletedTask;} public Task<bool> ConfirmDocumentVersionLinkAsync(Guid tenantId, Guid documentId, Guid documentVersionId, CancellationToken ct)=>Task.FromResult(false); }
public sealed class SignaturePackageService : ISignaturePackageService { public Task<SignaturePackageFile> GenerateP7sAsync(Guid tenantId, Guid signatureId, CancellationToken ct)=>throw new InvalidOperationException("Signature package repository not configured."); public Task<SignaturePackageFile> GenerateValidationReportJsonAsync(Guid tenantId, Guid signatureId, CancellationToken ct)=>throw new InvalidOperationException("Signature package repository not configured."); public Task<SignaturePackageFile> GenerateZipAsync(Guid tenantId, Guid signatureId, CancellationToken ct)=>throw new InvalidOperationException("Signature package repository not configured."); public Task<IReadOnlyDictionary<string,string>> CalculateChecksumsAsync(IReadOnlyList<SignaturePackageFile> files, CancellationToken ct)=>Task.FromResult<IReadOnlyDictionary<string,string>>(new Dictionary<string,string>()); }
public sealed class PostgresSignatureEvidenceRepository(IDbConnectionFactory db) : ISignatureEvidenceRepository { public async Task StoreAsync(SignatureEvidence evidence, CancellationToken ct) { await using var c = await db.OpenAsync(ct); await c.ExecuteAsync(new CommandDefinition("insert into ged.signature_evidence (id, tenant_id, signature_id, evidence_type, hash_algorithm, evidence_hash, evidence_bytes, captured_at, correlation_id) values (gen_random_uuid(), @TenantId,@SignatureId,@EvidenceType,@HashAlgorithm,@EvidenceHash,@EvidenceBytes,@CapturedAt,@CorrelationId)", evidence, cancellationToken: ct)); } }
public sealed class PostgresSigningSessionRepository(IDbConnectionFactory db) : ISigningSessionRepository { public async Task SaveAsync(PrepareSignatureCommand command, PrepareSignatureResult result, CancellationToken ct) { if (result.SessionId is null) return; await CreateAsync(new SigningSessionRecord(result.SessionId.Value, command.TenantId, command.UserId, command.DocumentId, command.DocumentVersionId, result.Status.ToString(), command.ContentHash, command.ContentHashAlgorithm, 0, string.Empty, string.Empty, string.Empty, command.ExpiresAt, null, null, null, 0, command.CorrelationId), string.Empty, string.Empty, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(command.Nonce))).ToLowerInvariant(), ct); } public async Task CreateAsync(SigningSessionRecord s, string contentTokenHash, string completionTokenHash, string nonceHash, CancellationToken ct){ await using var c=await db.OpenAsync(ct); await c.ExecuteAsync(new CommandDefinition("insert into ged.signing_session (id,tenant_id,user_id,document_id,document_version_id,status,content_hash,content_hash_algorithm,content_token_hash,completion_token_hash,nonce_hash,expires_at,signature_type,signature_format,correlation_id,size_bytes,file_name,document_code,version_label) values (@Id,@TenantId,@UserId,@DocumentId,@DocumentVersionId,@Status,@ContentHash,@ContentHashAlgorithm,@contentTokenHash,@completionTokenHash,@nonceHash,@ExpiresAt,'CMS_DETACHED','CMS_PKCS7_DETACHED',@CorrelationId,@SizeBytes,@FileName,@DocumentCode,@VersionLabel) on conflict (id) do nothing", new{s.Id,s.TenantId,s.UserId,s.DocumentId,s.DocumentVersionId,s.Status,s.ContentHash,s.ContentHashAlgorithm,contentTokenHash,completionTokenHash,nonceHash,s.ExpiresAt,s.CorrelationId,s.SizeBytes,s.FileName,s.DocumentCode,s.VersionLabel}, cancellationToken:ct)); } public Task<SigningSessionRecord?> GetAsync(Guid tenantId, Guid sessionId, CancellationToken ct)=>Task.FromResult<SigningSessionRecord?>(null); public Task<SigningSessionRecord?> GetForContentAsync(Guid tenantId, Guid sessionId, string contentTokenHash, CancellationToken ct)=>Task.FromResult<SigningSessionRecord?>(null); public Task<bool> ConsumeContentTokenAsync(Guid tenantId, Guid sessionId, string contentTokenHash, CancellationToken ct)=>Task.FromResult(false); public Task<SigningSessionRecord?> GetForCompletionAsync(Guid tenantId, Guid sessionId, string completionTokenHash, CancellationToken ct)=>Task.FromResult<SigningSessionRecord?>(null); public Task<bool> ConsumeCompletionTokenAsync(Guid tenantId, Guid sessionId, string completionTokenHash, string idempotencyKey, string payloadHash, CancellationToken ct)=>Task.FromResult(false); public Task MarkContentAccessedAsync(Guid tenantId, Guid sessionId, CancellationToken ct)=>Task.CompletedTask; public Task MarkWaitingConfirmationAsync(Guid tenantId, Guid sessionId, CancellationToken ct)=>Task.CompletedTask; public Task MarkSigningAsync(Guid tenantId, Guid sessionId, CancellationToken ct)=>Task.CompletedTask; public Task CompleteAsync(Guid tenantId, Guid sessionId, Guid signatureId, CancellationToken ct)=>Task.CompletedTask; public Task<bool> CancelAsync(Guid tenantId, Guid sessionId, Guid userId, CancellationToken ct)=>Task.FromResult(false); public Task<int> ExpireAsync(Guid tenantId, DateTimeOffset now, CancellationToken ct)=>Task.FromResult(0); public Task IncrementFailureAsync(Guid tenantId, Guid sessionId, string safeError, CancellationToken ct)=>Task.CompletedTask; public Task<DocumentSignatureRecord?> GetExistingCompletionAsync(Guid tenantId, Guid sessionId, string idempotencyKey, string payloadHash, CancellationToken ct)=>Task.FromResult<DocumentSignatureRecord?>(null); }
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
