using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Common.Storage;
using System.IO.Compression;
using System.Text.Json;
using InovaGed.Application.Signatures;

namespace InovaGed.Infrastructure.Signatures;

public sealed class CmsDetachedSignatureValidationService : ISignatureValidationService
{

    private static async Task<SigningSessionRecord?> LockSessionForCompletionAsync(SigningTransactionContext tx, Guid tenantId, Guid sessionId) =>
        await tx.Connection.QuerySingleOrDefaultAsync<SigningSessionRecord>(new CommandDefinition(PostgresSigningSessionRepository.SelectForUpdateSql, new { tenantId, sessionId }, tx.Transaction, cancellationToken: tx.CancellationToken));
    private static async Task<DocumentSignatureRecord?> FindExistingCompletionAsync(SigningTransactionContext tx, Guid tenantId, Guid sessionId, string idempotencyKey, string payloadHash) =>
        await tx.Connection.QuerySingleOrDefaultAsync<DocumentSignatureRecord>(new CommandDefinition(PostgresSigningSessionRepository.ExistingCompletionSql, new { tenantId, sessionId, idempotencyKey, payloadHash }, tx.Transaction, cancellationToken: tx.CancellationToken));
    private static async Task<bool> ConsumeCompletionTokenAsync(SigningTransactionContext tx, Guid tenantId, Guid sessionId, string completionTokenHash, string idempotencyKey, string payloadHash) =>
        await tx.Connection.ExecuteScalarAsync<int>(new CommandDefinition("update ged.signing_session set status='VALIDATING', completion_token_consumed_at=coalesce(completion_token_consumed_at,now()), completion_idempotency_key=@idempotencyKey, idempotency_payload_hash=@payloadHash where tenant_id=@tenantId and id=@sessionId and status in ('CONTENT_ACCESSED','WAITING_CONFIRMATION','SIGNING') and expires_at>now() and completion_token_hash=@completionTokenHash and (completion_idempotency_key is null or (completion_idempotency_key=@idempotencyKey and idempotency_payload_hash=@payloadHash)) returning 1", new{tenantId,sessionId,completionTokenHash,idempotencyKey,payloadHash}, tx.Transaction, cancellationToken:tx.CancellationToken))==1;
    private static async Task<Guid> CreateSignatureAsync(SigningTransactionContext tx, DocumentSignatureRecord s) =>
        await tx.Connection.ExecuteScalarAsync<Guid>(new CommandDefinition("insert into ged.document_signature (id,tenant_id,signing_session_id,document_id,document_version_id,signature_type,signature_format,signature_profile,signature_source,cryptographic_status,certificate_status,trust_status,validation_status,conformity_status,cms_bytes,cms_sha256,content_sha256,certificate_der,certificate_chain_der,engine_version,correlation_id,created_at) values (@Id,@TenantId,@SessionId,@DocumentId,@DocumentVersionId,@SignatureType,@SignatureFormat,@SignatureProfile,@SignatureSource,@CryptographicStatus,@CertificateStatus,@TrustStatus,@ValidationStatus,@ConformityStatus,@CmsBytes,@CmsSha256,@ContentSha256,@CertificateDer,@CertificateChainDer,@EngineVersion,@CorrelationId,@CreatedAt) on conflict (tenant_id, signing_session_id) do update set signing_session_id=excluded.signing_session_id returning id", new { s.Id,s.TenantId,s.SessionId,s.DocumentId,s.DocumentVersionId,s.SignatureType,s.SignatureFormat,s.SignatureProfile,s.SignatureSource,s.CryptographicStatus,s.CertificateStatus,s.TrustStatus,s.ValidationStatus,s.ConformityStatus,s.CmsBytes,s.CmsSha256,s.ContentSha256,s.CertificateDer,CertificateChainDer=s.CertificateChainDer.ToArray(),s.EngineVersion,s.CorrelationId,s.CreatedAt }, tx.Transaction, cancellationToken:tx.CancellationToken));
    private static Task CreateValidationRunAsync(SigningTransactionContext tx, SignatureValidationRunRecord r) => tx.Connection.ExecuteAsync(new CommandDefinition("insert into ged.signature_validation_run(id,tenant_id,signature_id,cryptographic_status,certificate_status,trust_status,validation_status,conformity_status,engine_version,validated_at,correlation_id) values(@Id,@TenantId,@SignatureId,@CryptographicStatus,@CertificateStatus,@TrustStatus,@ValidationStatus,@ConformityStatus,@EngineVersion,@ValidatedAt,@CorrelationId) on conflict (id) do nothing", r, tx.Transaction, cancellationToken:tx.CancellationToken));
    private static async Task StoreChecksAsync(SigningTransactionContext tx, Guid tenantId, Guid validationRunId, IReadOnlyList<SignatureValidationCheck> checks){ var order=0; foreach(var c in checks) await tx.Connection.ExecuteAsync(new CommandDefinition("insert into ged.signature_validation_check(id,tenant_id,validation_run_id,name,status,message,evidence_hash,check_order,created_at) values(gen_random_uuid(),@tenantId,@validationRunId,@Name,@Status,@Message,@EvidenceHash,@order,now()) on conflict do nothing", new{tenantId,validationRunId,c.Name,Status=c.Status.ToString(),c.Message,c.EvidenceHash,order=order++}, tx.Transaction, cancellationToken:tx.CancellationToken)); }
    private static async Task StoreChainAsync(SigningTransactionContext tx, Guid tenantId, Guid validationRunId, IReadOnlyList<byte[]> chainDer){ for(var i=0;i<chainDer.Count;i++){ var hash=Convert.ToHexString(SHA256.HashData(chainDer[i])).ToLowerInvariant(); await tx.Connection.ExecuteAsync(new CommandDefinition("insert into ged.signature_certificate_chain(id,tenant_id,validation_run_id,chain_order,certificate_der,certificate_sha256,created_at) values(gen_random_uuid(),@tenantId,@validationRunId,@i,@der,@hash,now()) on conflict do nothing", new{tenantId,validationRunId,i,der=chainDer[i],hash}, tx.Transaction, cancellationToken:tx.CancellationToken));}}
    private static Task StoreEvidenceAsync(SigningTransactionContext tx, SignatureEvidence e) => tx.Connection.ExecuteAsync(new CommandDefinition("insert into ged.signature_evidence (id, tenant_id, signature_id, evidence_type, hash_algorithm, evidence_hash, evidence_bytes, captured_at, correlation_id) values (gen_random_uuid(), @TenantId,@SignatureId,@EvidenceType,@HashAlgorithm,@EvidenceHash,@EvidenceBytes,@CapturedAt,@CorrelationId) on conflict (tenant_id,signature_id,evidence_type,evidence_hash) do nothing", e, tx.Transaction, cancellationToken:tx.CancellationToken));
    private static Task RegisterEventAsync(SigningTransactionContext tx, SignatureEventRecord e) => tx.Connection.ExecuteAsync(new CommandDefinition("insert into ged.signature_event(id,tenant_id,signing_session_id,signature_id,event_type,safe_message,correlation_id,created_at) values(@Id,@TenantId,@SessionId,@SignatureId,@EventType,@SafeMessage,@CorrelationId,@CreatedAt) on conflict do nothing", e, tx.Transaction, cancellationToken:tx.CancellationToken));
    private static Task CompleteSessionAsync(SigningTransactionContext tx, Guid tenantId, Guid sessionId, Guid signatureId) => tx.Connection.ExecuteAsync(new CommandDefinition("update ged.signing_session set status='COMPLETED',completed_at=now(),signature_id=@signatureId where tenant_id=@tenantId and id=@sessionId and status='VALIDATING'", new{tenantId,sessionId,signatureId}, tx.Transaction, cancellationToken:tx.CancellationToken));
    private static IReadOnlyList<SignatureValidationCheck> EnsureMandatoryChecks(IReadOnlyList<SignatureValidationCheck> checks){ var list=checks.ToList(); foreach(var name in new[]{"CMS_STRUCTURE","CMS_DETACHED","SIGNER_COUNT","SIGNATURE_MATH","MESSAGE_DIGEST","CERTIFICATE_EMBEDDED","CERTIFICATE_MATCH","DIGEST_ALGORITHM","SIGNATURE_ALGORITHM","CERTIFICATE_VALIDITY","CERTIFICATE_KEY_USAGE","CERTIFICATE_EXTENDED_KEY_USAGE","CHAIN_BUILD","REVOCATION_NOT_EVALUATED","POLICY_NOT_EVALUATED","TIMESTAMP_NOT_PRESENT"}) if(!list.Any(c=>c.Name==name)) list.Add(new SignatureValidationCheck(name, name.EndsWith("NOT_EVALUATED")||name=="TIMESTAMP_NOT_PRESENT"?SignatureValidationStatus.INDETERMINATE:SignatureValidationStatus.NOT_VERIFIABLE, "Check registrado para homologação CMS RC3.")); return list; }
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
    public Task<ContentCapabilityResult?> ResolveAndConsumeContentCapabilityAsync(Guid sessionId, string contentTokenHash, CancellationToken ct) => Task.FromResult<ContentCapabilityResult?>(null);
    public Task<SigningSessionRecord?> ResolveContentCapabilityAsync(Guid sessionId, string contentTokenHash, CancellationToken ct) => Task.FromResult<SigningSessionRecord?>(null);
    public Task<SigningSessionRecord?> ConsumeContentCapabilityAsync(Guid sessionId, string contentTokenHash, CancellationToken ct) => Task.FromResult<SigningSessionRecord?>(null);
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
    private const string Select = "select id,tenant_id TenantId,signing_session_id SessionId,document_id DocumentId,document_version_id DocumentVersionId,signature_type SignatureType,signature_format SignatureFormat,signature_profile SignatureProfile,signature_source SignatureSource,cryptographic_status CryptographicStatus,coalesce(certificate_status,'NOT_VERIFIABLE') CertificateStatus,coalesce(trust_status,'NOT_VERIFIABLE') TrustStatus,validation_status ValidationStatus,conformity_status ConformityStatus,cms_sha256 CmsSha256,content_sha256 ContentSha256,cms_bytes CmsBytes,certificate_der CertificateDer,coalesce(certificate_chain_der,ARRAY[]::bytea[]) CertificateChainDer,engine_version EngineVersion,correlation_id CorrelationId,created_at CreatedAt from ged.document_signature";
    public async Task<Guid> CreateAsync(DocumentSignatureRecord signature, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        var id = await c.ExecuteScalarAsync<Guid?>(new CommandDefinition(@"""
insert into ged.document_signature (id,tenant_id,signing_session_id,document_id,document_version_id,signature_type,signature_format,signature_profile,signature_source,cryptographic_status,certificate_status,trust_status,validation_status,conformity_status,cms_bytes,cms_sha256,content_sha256,certificate_der,certificate_chain_der,engine_version,correlation_id,created_at)
values (@Id,@TenantId,@SessionId,@DocumentId,@DocumentVersionId,@SignatureType,@SignatureFormat,@SignatureProfile,@SignatureSource,@CryptographicStatus,@CertificateStatus,@TrustStatus,@ValidationStatus,@ConformityStatus,@CmsBytes,@CmsSha256,@ContentSha256,@CertificateDer,@CertificateChainDer,@EngineVersion,@CorrelationId,@CreatedAt)
on conflict (tenant_id, signing_session_id) do update set signing_session_id=excluded.signing_session_id returning id
""", new { signature.Id, signature.TenantId, signature.SessionId, signature.DocumentId, signature.DocumentVersionId, signature.SignatureType, signature.SignatureFormat, signature.SignatureProfile, signature.SignatureSource, signature.CryptographicStatus, signature.CertificateStatus, signature.TrustStatus, signature.ValidationStatus, signature.ConformityStatus, signature.CmsBytes, signature.CmsSha256, signature.ContentSha256, signature.CertificateDer, CertificateChainDer=signature.CertificateChainDer.ToArray(), signature.EngineVersion, signature.CorrelationId, signature.CreatedAt }, cancellationToken: ct));
        return id ?? signature.Id;
    }
    public async Task<DocumentSignatureRecord?> GetAsync(Guid tenantId, Guid signatureId, CancellationToken ct) { await using var c=await db.OpenAsync(ct); return await c.QuerySingleOrDefaultAsync<DocumentSignatureRecord>(new CommandDefinition(Select+" where tenant_id=@tenantId and id=@signatureId", new{tenantId,signatureId}, cancellationToken:ct)); }
    public async Task<IReadOnlyList<DocumentSignatureRecord>> ListByDocumentAsync(Guid tenantId, Guid documentId, CancellationToken ct) { await using var c=await db.OpenAsync(ct); return (await c.QueryAsync<DocumentSignatureRecord>(new CommandDefinition(Select+" where tenant_id=@tenantId and document_id=@documentId order by created_at desc", new{tenantId,documentId}, cancellationToken:ct))).ToList(); }
    public async Task<IReadOnlyList<DocumentSignatureRecord>> ListByVersionAsync(Guid tenantId, Guid documentId, Guid documentVersionId, CancellationToken ct) { await using var c=await db.OpenAsync(ct); return (await c.QueryAsync<DocumentSignatureRecord>(new CommandDefinition(Select+" where tenant_id=@tenantId and document_id=@documentId and document_version_id=@documentVersionId order by created_at desc", new{tenantId,documentId,documentVersionId}, cancellationToken:ct))).ToList(); }
    public async Task<byte[]?> GetCmsBytesAsync(Guid tenantId, Guid signatureId, CancellationToken ct) { await using var c=await db.OpenAsync(ct); return await c.ExecuteScalarAsync<byte[]>(new CommandDefinition("select cms_bytes from ged.document_signature where tenant_id=@tenantId and id=@signatureId", new{tenantId,signatureId}, cancellationToken:ct)); }
    public async Task<byte[]?> GetCertificateAsync(Guid tenantId, Guid signatureId, CancellationToken ct) { await using var c=await db.OpenAsync(ct); return await c.ExecuteScalarAsync<byte[]>(new CommandDefinition("select certificate_der from ged.document_signature where tenant_id=@tenantId and id=@signatureId", new{tenantId,signatureId}, cancellationToken:ct)); }
    public async Task<bool> ExistsForSessionAsync(Guid tenantId, Guid sessionId, CancellationToken ct) { await using var c=await db.OpenAsync(ct); return await c.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from ged.document_signature where tenant_id=@tenantId and signing_session_id=@sessionId)", new{tenantId,sessionId}, cancellationToken:ct)); }
}

public sealed class PostgresSignatureValidationRepository(IDbConnectionFactory db) : ISignatureValidationRepository
{
    public async Task<Guid> CreateRunAsync(SignatureValidationRunRecord run, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await c.ExecuteAsync(new CommandDefinition(@"""
insert into ged.signature_validation_run(id,tenant_id,signature_id,cryptographic_status,certificate_status,trust_status,validation_status,conformity_status,engine_version,validated_at,correlation_id)
values(@Id,@TenantId,@SignatureId,@CryptographicStatus,@CertificateStatus,@TrustStatus,@ValidationStatus,@ConformityStatus,@EngineVersion,@ValidatedAt,@CorrelationId)
on conflict (id) do nothing
""", run, cancellationToken: ct));
        return run.Id;
    }

    public async Task StoreChecksAsync(Guid tenantId, Guid validationRunId, IReadOnlyList<SignatureValidationCheck> checks, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        var order = 0;
        foreach (var check in checks)
            await c.ExecuteAsync(new CommandDefinition(@"""
insert into ged.signature_validation_check(id,tenant_id,validation_run_id,name,status,message,evidence_hash,check_order,created_at)
values(gen_random_uuid(),@tenantId,@validationRunId,@Name,@Status,@Message,@EvidenceHash,@order,now())
on conflict do nothing
""", new { tenantId, validationRunId, check.Name, Status = check.Status.ToString(), check.Message, check.EvidenceHash, order = order++ }, cancellationToken: ct));
    }

    public async Task StoreChainAsync(Guid tenantId, Guid validationRunId, IReadOnlyList<byte[]> chainDer, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        for (var i = 0; i < chainDer.Count; i++)
        {
            var hash = Convert.ToHexString(SHA256.HashData(chainDer[i])).ToLowerInvariant();
            await c.ExecuteAsync(new CommandDefinition(@"""
insert into ged.signature_certificate_chain(id,tenant_id,validation_run_id,chain_order,certificate_der,certificate_sha256,created_at)
values(gen_random_uuid(),@tenantId,@validationRunId,@i,@der,@hash,now())
on conflict do nothing
""", new { tenantId, validationRunId, i, der = chainDer[i], hash }, cancellationToken: ct));
        }
    }

    public async Task<SignatureValidationRunRecord?> GetLatestAsync(Guid tenantId, Guid signatureId, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.QuerySingleOrDefaultAsync<SignatureValidationRunRecord>(new CommandDefinition(@"""
select id, tenant_id TenantId, signature_id SignatureId, cryptographic_status CryptographicStatus, coalesce(certificate_status,'NOT_VERIFIABLE') CertificateStatus, coalesce(trust_status,'NOT_VERIFIABLE') TrustStatus, validation_status ValidationStatus,
       conformity_status ConformityStatus, engine_version EngineVersion, validated_at ValidatedAt, correlation_id CorrelationId
from ged.signature_validation_run where tenant_id=@tenantId and signature_id=@signatureId order by validated_at desc limit 1
""", new { tenantId, signatureId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<SignatureValidationRunRecord>> ListHistoryAsync(Guid tenantId, Guid signatureId, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return (await c.QueryAsync<SignatureValidationRunRecord>(new CommandDefinition(@"""
select id, tenant_id TenantId, signature_id SignatureId, cryptographic_status CryptographicStatus, coalesce(certificate_status,'NOT_VERIFIABLE') CertificateStatus, coalesce(trust_status,'NOT_VERIFIABLE') TrustStatus, validation_status ValidationStatus,
       conformity_status ConformityStatus, engine_version EngineVersion, validated_at ValidatedAt, correlation_id CorrelationId
from ged.signature_validation_run where tenant_id=@tenantId and signature_id=@signatureId order by validated_at desc
""", new { tenantId, signatureId }, cancellationToken: ct))).ToList();
    }
}

public sealed class PostgresSignatureEventRepository(IDbConnectionFactory db) : ISignatureEventRepository
{
    public async Task RegisterAsync(SignatureEventRecord evt, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await c.ExecuteAsync(new CommandDefinition("insert into ged.signature_event(id,tenant_id,signing_session_id,signature_id,event_type,safe_message,correlation_id,created_at) values(@Id,@TenantId,@SessionId,@SignatureId,@EventType,@SafeMessage,@CorrelationId,@CreatedAt) on conflict do nothing", evt, cancellationToken: ct));
    }
    public async Task<IReadOnlyList<SignatureEventRecord>> ListBySessionAsync(Guid tenantId, Guid sessionId, CancellationToken ct)
    {
        await using var c=await db.OpenAsync(ct);
        return (await c.QueryAsync<SignatureEventRecord>(new CommandDefinition("select id,tenant_id TenantId,signing_session_id SessionId,signature_id SignatureId,event_type EventType,safe_message SafeMessage,correlation_id CorrelationId,created_at CreatedAt from ged.signature_event where tenant_id=@tenantId and signing_session_id=@sessionId order by created_at", new{tenantId,sessionId}, cancellationToken:ct))).ToList();
    }
    public async Task<IReadOnlyList<SignatureEventRecord>> ListBySignatureAsync(Guid tenantId, Guid signatureId, CancellationToken ct)
    {
        await using var c=await db.OpenAsync(ct);
        return (await c.QueryAsync<SignatureEventRecord>(new CommandDefinition("select id,tenant_id TenantId,signing_session_id SessionId,signature_id SignatureId,event_type EventType,safe_message SafeMessage,correlation_id CorrelationId,created_at CreatedAt from ged.signature_event where tenant_id=@tenantId and signature_id=@signatureId order by created_at", new{tenantId,signatureId}, cancellationToken:ct))).ToList();
    }
}

public sealed class DocumentVersionSigningContentService(IDbConnectionFactory db, IFileStorage storage, IOptions<DigitalSignatureOptions> options) : IDocumentVersionSigningContentService
{
    public async Task<SigningContentMetadata?> LocateVersionAsync(Guid tenantId, Guid documentId, Guid documentVersionId, CancellationToken ct)
    {
        await using var c=await db.OpenAsync(ct);
        return await c.QuerySingleOrDefaultAsync<SigningContentMetadata>(new CommandDefinition(@"""
select v.tenant_id TenantId, v.document_id DocumentId, v.id DocumentVersionId, coalesce(v.file_name,'documento.bin') FileName,
       coalesce(d.code,d.id::text) DocumentCode, coalesce(v.version_number::text,v.id::text) VersionLabel,
       coalesce(v.content_type,'application/octet-stream') ContentType, coalesce(v.size_bytes,0) SizeBytes
from ged.document_version v join ged.document d on d.tenant_id=v.tenant_id and d.id=v.document_id
where v.tenant_id=@tenantId and v.document_id=@documentId and v.id=@documentVersionId and coalesce(v.reg_status,'A')='A' and coalesce(d.reg_status,'A')='A'
""", new{tenantId,documentId,documentVersionId}, cancellationToken:ct));
    }
    public async Task<bool> ValidateTenantAsync(Guid tenantId, Guid documentId, Guid documentVersionId, CancellationToken ct) => await LocateVersionAsync(tenantId, documentId, documentVersionId, ct) is not null;
    public async Task<SigningContentMetadata> GetMetadataAsync(Guid tenantId, Guid documentId, Guid documentVersionId, CancellationToken ct) => await LocateVersionAsync(tenantId, documentId, documentVersionId, ct) ?? throw new InvalidOperationException("Versão documental não localizada para assinatura CMS.");
    public async Task<Stream> OpenReadAsync(Guid tenantId, Guid documentId, Guid documentVersionId, CancellationToken ct)
    {
        await using var c=await db.OpenAsync(ct);
        var path=await c.ExecuteScalarAsync<string>(new CommandDefinition("select storage_path from ged.document_version where tenant_id=@tenantId and document_id=@documentId and id=@documentVersionId and nullif(storage_path,'') is not null", new{tenantId,documentId,documentVersionId}, cancellationToken:ct));
        if (string.IsNullOrWhiteSpace(path) || !await storage.ExistsAsync(path, ct)) throw new FileNotFoundException("Arquivo físico da versão não encontrado.");
        return await storage.OpenReadAsync(path, ct);
    }
    public async Task<string> CalculateSha256Async(Stream content, CancellationToken ct){ using var sha=SHA256.Create(); return Convert.ToHexString(await sha.ComputeHashAsync(content, ct)).ToLowerInvariant(); }
    public Task ValidateSizeAsync(long sizeBytes, CancellationToken ct){ if(sizeBytes<=0 || sizeBytes>(long)options.Value.MaxDocumentSizeMb*1024*1024) throw new InvalidOperationException("Tamanho de conteúdo inválido para assinatura CMS."); return Task.CompletedTask;}
    public async Task<bool> ConfirmDocumentVersionLinkAsync(Guid tenantId, Guid documentId, Guid documentVersionId, CancellationToken ct) => await ValidateTenantAsync(tenantId, documentId, documentVersionId, ct);
}

public sealed class SignaturePackageService(ISignatureRepository signatures, ISignatureValidationRepository validations, IDocumentVersionSigningContentService content) : ISignaturePackageService
{
    public async Task<SignaturePackageFile> GenerateP7sAsync(Guid tenantId, Guid signatureId, CancellationToken ct)
    {
        var bytes = await signatures.GetCmsBytesAsync(tenantId, signatureId, ct) ?? throw new FileNotFoundException("CMS não encontrado.");
        return new SignaturePackageFile("assinatura.p7s", "application/pkcs7-signature", new MemoryStream(bytes, writable:false), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }
    public async Task<SignaturePackageFile> GenerateValidationReportJsonAsync(Guid tenantId, Guid signatureId, CancellationToken ct)
    {
        var sig = await signatures.GetAsync(tenantId, signatureId, ct) ?? throw new FileNotFoundException("Assinatura não encontrada.");
        var run = await validations.GetLatestAsync(tenantId, signatureId, ct);
        var json = JsonSerializer.SerializeToUtf8Bytes(new { signature=sig, validation=run, notice="CMS destacado; ICP-Brasil, revogação e carimbo do tempo não avaliados nesta etapa." }, new JsonSerializerOptions{WriteIndented=true});
        return new SignaturePackageFile("validation-report.json", "application/json", new MemoryStream(json, writable:false), Convert.ToHexString(SHA256.HashData(json)).ToLowerInvariant());
    }
    public async Task<SignaturePackageFile> GenerateZipAsync(Guid tenantId, Guid signatureId, CancellationToken ct)
    {
        var sig = await signatures.GetAsync(tenantId, signatureId, ct) ?? throw new FileNotFoundException("Assinatura não encontrada.");
        await using var original = await content.OpenReadAsync(tenantId, sig.DocumentId, sig.DocumentVersionId, ct);
        var p7s = await GenerateP7sAsync(tenantId, signatureId, ct); var report = await GenerateValidationReportJsonAsync(tenantId, signatureId, ct);
        var ms = new MemoryStream(); using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen:true))
        { await Add(zip,"documento-original.bin",original,ct); await Add(zip,p7s.FileName,p7s.Content,ct); await Add(zip,report.FileName,report.Content,ct); var readme="Assinatura CMS destacada. Conformidade ICP-Brasil não avaliada. Revogação não avaliada. Carimbo do tempo inexistente nesta etapa."; await Write(zip,"README.txt",readme,ct); }
        ms.Position=0; return new SignaturePackageFile("validation-package.zip", "application/zip", ms, Convert.ToHexString(SHA256.HashData(ms.ToArray())).ToLowerInvariant());
    }
    public async Task<IReadOnlyDictionary<string,string>> CalculateChecksumsAsync(IReadOnlyList<SignaturePackageFile> files, CancellationToken ct){ var d=new Dictionary<string,string>(); foreach(var f in files){ using var sha=SHA256.Create(); f.Content.Position=0; d[f.FileName]=Convert.ToHexString(await sha.ComputeHashAsync(f.Content,ct)).ToLowerInvariant(); f.Content.Position=0;} return d; }
    static async Task Add(ZipArchive z,string n,Stream s,CancellationToken ct){ var e=z.CreateEntry(n); await using var o=e.Open(); s.Position=0; await s.CopyToAsync(o,ct); }
    static async Task Write(ZipArchive z,string n,string t,CancellationToken ct){ var e=z.CreateEntry(n); await using var o=e.Open(); await o.WriteAsync(Encoding.UTF8.GetBytes(t),ct); }
}

public sealed class PostgresSignatureEvidenceRepository(IDbConnectionFactory db) : ISignatureEvidenceRepository { public async Task StoreAsync(SignatureEvidence evidence, CancellationToken ct) { if (evidence.EvidenceBytes?.Length > 10_000_000) throw new InvalidOperationException("Evidência excede limite permitido."); await using var c = await db.OpenAsync(ct); await c.ExecuteAsync(new CommandDefinition("insert into ged.signature_evidence (id, tenant_id, signature_id, evidence_type, hash_algorithm, evidence_hash, evidence_bytes, captured_at, correlation_id) values (gen_random_uuid(), @TenantId,@SignatureId,@EvidenceType,@HashAlgorithm,@EvidenceHash,@EvidenceBytes,@CapturedAt,@CorrelationId) on conflict (tenant_id,signature_id,evidence_type,evidence_hash) do nothing", evidence, cancellationToken: ct)); } }

public sealed class PostgresSigningSessionRepository(IDbConnectionFactory db) : ISigningSessionRepository
{
    public const string SelectForUpdateSql = "select id,tenant_id TenantId,user_id UserId,document_id DocumentId,document_version_id DocumentVersionId,status,content_hash ContentHash,content_hash_algorithm ContentHashAlgorithm,coalesce(size_bytes,0) SizeBytes,coalesce(file_name,'') FileName,coalesce(document_code,'') DocumentCode,coalesce(version_label,'') VersionLabel,expires_at ExpiresAt,first_content_accessed_at FirstContentAccessedAt,completed_at CompletedAt,cancelled_at CancelledAt,coalesce(failure_count,failed_attempts,0) FailureCount,correlation_id CorrelationId,signature_id SignatureId,safe_error SafeError from ged.signing_session where tenant_id=@tenantId and id=@sessionId for update";
    public const string ExistingCompletionSql = "select s.id,s.tenant_id TenantId,s.signing_session_id SessionId,s.document_id DocumentId,s.document_version_id DocumentVersionId,s.signature_type SignatureType,s.signature_format SignatureFormat,s.signature_profile SignatureProfile,s.signature_source SignatureSource,s.cryptographic_status CryptographicStatus,coalesce(s.certificate_status,'NOT_VERIFIABLE') CertificateStatus,coalesce(s.trust_status,'NOT_VERIFIABLE') TrustStatus,s.validation_status ValidationStatus,s.conformity_status ConformityStatus,s.cms_sha256 CmsSha256,s.content_sha256 ContentSha256,s.cms_bytes CmsBytes,s.certificate_der CertificateDer,coalesce(s.certificate_chain_der,ARRAY[]::bytea[]) CertificateChainDer,s.engine_version EngineVersion,s.correlation_id CorrelationId,s.created_at CreatedAt from ged.document_signature s join ged.signing_session ss on ss.tenant_id=s.tenant_id and ss.id=s.signing_session_id where ss.tenant_id=@tenantId and ss.id=@sessionId and ss.completion_idempotency_key=@idempotencyKey and ss.idempotency_payload_hash=@payloadHash";
    const string SelectSql = "select id,tenant_id TenantId,user_id UserId,document_id DocumentId,document_version_id DocumentVersionId,status,content_hash ContentHash,content_hash_algorithm ContentHashAlgorithm,coalesce(size_bytes,0) SizeBytes,coalesce(file_name,'') FileName,coalesce(document_code,'') DocumentCode,coalesce(version_label,'') VersionLabel,expires_at ExpiresAt,first_content_accessed_at FirstContentAccessedAt,completed_at CompletedAt,cancelled_at CancelledAt,coalesce(failure_count,failed_attempts,0) FailureCount,correlation_id CorrelationId,signature_id SignatureId,safe_error SafeError from ged.signing_session";
    public async Task SaveAsync(PrepareSignatureCommand command, PrepareSignatureResult result, CancellationToken ct) { if (result.SessionId is null) return; await CreateAsync(new SigningSessionRecord(result.SessionId.Value, command.TenantId, command.UserId, command.DocumentId, command.DocumentVersionId, result.Status.ToString(), command.ContentHash, command.ContentHashAlgorithm, 0, string.Empty, string.Empty, string.Empty, command.ExpiresAt, null, null, null, 0, command.CorrelationId), string.Empty, string.Empty, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(command.Nonce))).ToLowerInvariant(), ct); }
    public async Task CreateAsync(SigningSessionRecord s, string contentTokenHash, string completionTokenHash, string nonceHash, CancellationToken ct){ await using var c=await db.OpenAsync(ct); await c.ExecuteAsync(new CommandDefinition(@"""insert into ged.signing_session (id,tenant_id,user_id,document_id,document_version_id,status,content_hash,content_hash_algorithm,content_token_hash,completion_token_hash,nonce_hash,expires_at,signature_type,signature_format,correlation_id,size_bytes,file_name,document_code,version_label,failure_count,failed_attempts) values (@Id,@TenantId,@UserId,@DocumentId,@DocumentVersionId,@Status,@ContentHash,@ContentHashAlgorithm,@contentTokenHash,@completionTokenHash,@nonceHash,@ExpiresAt,'CMS_DETACHED','CMS_PKCS7_DETACHED',@CorrelationId,@SizeBytes,@FileName,@DocumentCode,@VersionLabel,0,0) on conflict (id) do nothing""", new{s.Id,s.TenantId,s.UserId,s.DocumentId,s.DocumentVersionId,s.Status,s.ContentHash,s.ContentHashAlgorithm,contentTokenHash,completionTokenHash,nonceHash,s.ExpiresAt,s.CorrelationId,s.SizeBytes,s.FileName,s.DocumentCode,s.VersionLabel}, cancellationToken:ct)); }
    public async Task<SigningSessionRecord?> GetAsync(Guid tenantId, Guid sessionId, CancellationToken ct){ await using var c=await db.OpenAsync(ct); return await c.QuerySingleOrDefaultAsync<SigningSessionRecord>(new CommandDefinition(SelectSql+" where tenant_id=@tenantId and id=@sessionId", new{tenantId,sessionId}, cancellationToken:ct)); }
    public async Task<SigningSessionRecord?> GetForContentAsync(Guid tenantId, Guid sessionId, string contentTokenHash, CancellationToken ct){ await using var c=await db.OpenAsync(ct); return await c.QuerySingleOrDefaultAsync<SigningSessionRecord>(new CommandDefinition(SelectSql+" where tenant_id=@tenantId and id=@sessionId and (content_token_hash=@contentTokenHash or content_download_token_hash=@contentTokenHash)", new{tenantId,sessionId,contentTokenHash}, cancellationToken:ct)); }
    public async Task<ContentCapabilityResult?> ResolveAndConsumeContentCapabilityAsync(Guid sessionId, string contentTokenHash, CancellationToken ct)
    {
        var consumed = await ConsumeContentCapabilityAsync(sessionId, contentTokenHash, ct);
        if (consumed is null) return null;
        return new ContentCapabilityResult(consumed.TenantId, consumed.DocumentId, consumed.DocumentVersionId, consumed.FileName, "application/octet-stream", consumed.SizeBytes, consumed.ContentHash);
    }
    public async Task<SigningSessionRecord?> ResolveContentCapabilityAsync(Guid sessionId, string contentTokenHash, CancellationToken ct){ await using var c=await db.OpenAsync(ct); return await c.QuerySingleOrDefaultAsync<SigningSessionRecord>(new CommandDefinition(SelectSql+" where id=@sessionId and status in ('REQUESTED','WAITING_AGENT') and expires_at>now() and content_token_consumed_at is null and (content_token_hash=@contentTokenHash or content_download_token_hash=@contentTokenHash)", new{sessionId,contentTokenHash}, cancellationToken:ct)); }
    public async Task<SigningSessionRecord?> ConsumeContentCapabilityAsync(Guid sessionId, string contentTokenHash, CancellationToken ct){ await using var c=await db.OpenAsync(ct); return await c.QuerySingleOrDefaultAsync<SigningSessionRecord>(new CommandDefinition("with consumed as (update ged.signing_session set status='CONTENT_ACCESSED', first_content_accessed_at=coalesce(first_content_accessed_at,now()), content_token_consumed_at=now() where id=@sessionId and status in ('REQUESTED','WAITING_AGENT') and expires_at>now() and content_token_consumed_at is null and (content_token_hash=@contentTokenHash or content_download_token_hash=@contentTokenHash) returning *) select id,tenant_id TenantId,user_id UserId,document_id DocumentId,document_version_id DocumentVersionId,status,content_hash ContentHash,content_hash_algorithm ContentHashAlgorithm,coalesce(size_bytes,0) SizeBytes,coalesce(file_name,'') FileName,coalesce(document_code,'') DocumentCode,coalesce(version_label,'') VersionLabel,expires_at ExpiresAt,first_content_accessed_at FirstContentAccessedAt,completed_at CompletedAt,cancelled_at CancelledAt,coalesce(failure_count,failed_attempts,0) FailureCount,correlation_id CorrelationId,signature_id SignatureId,safe_error SafeError from consumed", new{sessionId,contentTokenHash}, cancellationToken:ct)); }
    public async Task<bool> ConsumeContentTokenAsync(Guid tenantId, Guid sessionId, string contentTokenHash, CancellationToken ct){ return await ConsumeContentCapabilityAsync(sessionId, contentTokenHash, ct) is not null; }
    public async Task<SigningSessionRecord?> GetForCompletionAsync(Guid tenantId, Guid sessionId, string completionTokenHash, CancellationToken ct){ await using var c=await db.OpenAsync(ct); return await c.QuerySingleOrDefaultAsync<SigningSessionRecord>(new CommandDefinition(SelectSql+" where tenant_id=@tenantId and id=@sessionId and completion_token_hash=@completionTokenHash", new{tenantId,sessionId,completionTokenHash}, cancellationToken:ct)); }
    public async Task<bool> ConsumeCompletionTokenAsync(Guid tenantId, Guid sessionId, string completionTokenHash, string idempotencyKey, string payloadHash, CancellationToken ct){ await using var c=await db.OpenAsync(ct); await using var tx=await c.BeginTransactionAsync(ct); var ok=await c.ExecuteScalarAsync<int>(new CommandDefinition("update ged.signing_session set status='VALIDATING', completion_token_consumed_at=coalesce(completion_token_consumed_at,now()), completion_idempotency_key=@idempotencyKey, idempotency_payload_hash=@payloadHash where tenant_id=@tenantId and id=@sessionId and status in ('CONTENT_ACCESSED','WAITING_CONFIRMATION','SIGNING') and expires_at>now() and completion_token_hash=@completionTokenHash and (completion_idempotency_key is null or (completion_idempotency_key=@idempotencyKey and idempotency_payload_hash=@payloadHash)) returning 1", new{tenantId,sessionId,completionTokenHash,idempotencyKey,payloadHash}, tx, cancellationToken:ct)); await tx.CommitAsync(ct); return ok==1; }
    public Task MarkContentAccessedAsync(Guid tenantId, Guid sessionId, CancellationToken ct)=>SetStatus(tenantId,sessionId,"CONTENT_ACCESSED",ct);
    public Task MarkWaitingConfirmationAsync(Guid tenantId, Guid sessionId, CancellationToken ct)=>SetStatus(tenantId,sessionId,"WAITING_CONFIRMATION",ct);
    public Task MarkSigningAsync(Guid tenantId, Guid sessionId, CancellationToken ct)=>SetStatus(tenantId,sessionId,"SIGNING",ct);
    public async Task CompleteAsync(Guid tenantId, Guid sessionId, Guid signatureId, CancellationToken ct){ await using var c=await db.OpenAsync(ct); await c.ExecuteAsync(new CommandDefinition("update ged.signing_session set status='COMPLETED',completed_at=now(),signature_id=@signatureId where tenant_id=@tenantId and id=@sessionId and status='VALIDATING'", new{tenantId,sessionId,signatureId}, cancellationToken:ct)); }
    public async Task<bool> CancelAsync(Guid tenantId, Guid sessionId, Guid userId, CancellationToken ct){ await using var c=await db.OpenAsync(ct); return await c.ExecuteScalarAsync<int>(new CommandDefinition("update ged.signing_session set status='CANCELLED',cancelled_at=now() where tenant_id=@tenantId and id=@sessionId and user_id=@userId and status not in ('COMPLETED','CANCELLED','EXPIRED') returning 1", new{tenantId,sessionId,userId}, cancellationToken:ct))==1; }
    public async Task<int> ExpireAsync(Guid tenantId, DateTimeOffset now, CancellationToken ct){ await using var c=await db.OpenAsync(ct); return await c.ExecuteAsync(new CommandDefinition("update ged.signing_session set status='EXPIRED' where tenant_id=@tenantId and expires_at<=@now and status not in ('COMPLETED','CANCELLED','FAILED','EXPIRED')", new{tenantId,now}, cancellationToken:ct)); }
    public async Task IncrementFailureAsync(Guid tenantId, Guid sessionId, string safeError, CancellationToken ct){ await using var c=await db.OpenAsync(ct); await c.ExecuteAsync(new CommandDefinition("update ged.signing_session set status='FAILED', failure_count=coalesce(failure_count,0)+1, failed_attempts=coalesce(failed_attempts,0)+1, safe_error=left(@safeError,500) where tenant_id=@tenantId and id=@sessionId", new{tenantId,sessionId,safeError}, cancellationToken:ct)); }
    public async Task<DocumentSignatureRecord?> GetExistingCompletionAsync(Guid tenantId, Guid sessionId, string idempotencyKey, string payloadHash, CancellationToken ct){ await using var c=await db.OpenAsync(ct); return await c.QuerySingleOrDefaultAsync<DocumentSignatureRecord>(new CommandDefinition("select s.id,s.tenant_id TenantId,s.signing_session_id SessionId,s.document_id DocumentId,s.document_version_id DocumentVersionId,s.signature_type SignatureType,s.signature_format SignatureFormat,s.signature_profile SignatureProfile,s.signature_source SignatureSource,s.cryptographic_status CryptographicStatus,s.validation_status ValidationStatus,s.conformity_status ConformityStatus,s.cms_sha256 CmsSha256,s.content_sha256 ContentSha256,s.cms_bytes CmsBytes,s.certificate_der CertificateDer,s.engine_version EngineVersion,s.correlation_id CorrelationId,s.created_at CreatedAt from ged.document_signature s join ged.signing_session ss on ss.tenant_id=s.tenant_id and ss.id=s.signing_session_id where ss.tenant_id=@tenantId and ss.id=@sessionId and ss.completion_idempotency_key=@idempotencyKey and ss.idempotency_payload_hash=@payloadHash", new{tenantId,sessionId,idempotencyKey,payloadHash}, cancellationToken:ct)); }
    async Task SetStatus(Guid tenantId,Guid sessionId,string status,CancellationToken ct){ await using var c=await db.OpenAsync(ct); await c.ExecuteAsync(new CommandDefinition("update ged.signing_session set status=@status where tenant_id=@tenantId and id=@sessionId and status not in ('COMPLETED','CANCELLED','EXPIRED')", new{tenantId,sessionId,status}, cancellationToken:ct)); }
}
public sealed class CmsSigningOrchestrator(ISignatureValidationService validation, ISigningSessionRepository sessions, IDocumentVersionSigningContentService content, ISignatureRepository signatures, ISignatureValidationRepository validationRuns, ISignatureEvidenceRepository evidences, ISignatureEventRepository events, ISigningUnitOfWorkFactory unitOfWorkFactory, ISignatureValidationOutcomeFactory outcomeFactory, Microsoft.Extensions.Options.IOptions<DigitalSignatureOptions> options) : ISigningOrchestrator
{
    public async Task<CreateSigningSessionResponse> PrepareAsync(PrepareSigningSessionCommand command, CancellationToken ct)
    {
        var metadata = await content.GetMetadataAsync(command.TenantId, command.DocumentId, command.DocumentVersionId, ct);
        await content.ValidateSizeAsync(metadata.SizeBytes, ct);
        await using var stream = await content.OpenReadAsync(command.TenantId, command.DocumentId, command.DocumentVersionId, ct);
        var sha256 = await content.CalculateSha256Async(stream, ct);
        var contentToken = Token();
        var completionToken = Token();
        var nonce = Token();
        var expires = DateTimeOffset.UtcNow.AddSeconds(options.Value.SessionTtlSeconds);
        var sessionId = Guid.NewGuid();
        await sessions.CreateAsync(new SigningSessionRecord(sessionId, command.TenantId, command.UserId, command.DocumentId, command.DocumentVersionId, SigningProcessStatus.REQUESTED.ToString(), sha256, "SHA-256", metadata.SizeBytes, metadata.FileName, metadata.DocumentCode, metadata.VersionLabel, expires, null, null, null, 0, command.CorrelationId), Hash(contentToken), Hash(completionToken), Hash(nonce), ct);
        return new CreateSigningSessionResponse(sessionId, SigningProcessStatus.REQUESTED.ToString(), BuildContentUrl(sessionId), contentToken, completionToken, sha256, metadata.SizeBytes, metadata.FileName, metadata.DocumentCode, metadata.VersionLabel, expires, command.CorrelationId);
    }

    public async Task<CompleteSignatureResult> CompleteAsync(CompleteSigningSessionCommand command, CancellationToken ct)
    {
        var tenantId = command.TenantId;
        var userId = command.UserId;
        var completionTokenHash = Hash(command.CompletionToken);
        var idempotencyKey = command.IdempotencyKey;
        var payloadHash = ComputeCompletionPayloadHash(command);
        await using var uow = await unitOfWorkFactory.BeginAsync(ct);
        var tx = new SigningTransactionContext(uow.Connection, uow.Transaction, ct);
        try
        {
            var session = await LockSessionForCompletionAsync(tx, tenantId, command.SessionId);
            if (session is null) { await uow.RollbackAsync(); return new CompleteSignatureResult(false, null, SignatureValidationStatus.NOT_VERIFIABLE, "invalid_session_or_completion_token"); }
            var existing = await FindExistingCompletionAsync(tx, tenantId, command.SessionId, idempotencyKey, payloadHash);
            if (existing is not null) { await uow.CommitAsync(); return new CompleteSignatureResult(true, existing.Id, SignatureValidationStatus.INDETERMINATE, null); }
            if (session.UserId != userId) { await uow.RollbackAsync(); return new CompleteSignatureResult(false, null, SignatureValidationStatus.NOT_VERIFIABLE, "user_mismatch"); }
            if (!await ConsumeCompletionTokenAsync(tx, tenantId, command.SessionId, completionTokenHash, idempotencyKey, payloadHash))
            { await uow.RollbackAsync(); return new CompleteSignatureResult(false, null, SignatureValidationStatus.INVALID, "invalid_state_or_idempotency_conflict"); }

            await using var doc = await content.OpenReadAsync(tenantId, session.DocumentId, session.DocumentVersionId, ct);
            var currentHash = await content.CalculateSha256Async(doc, ct);
            if (!string.Equals(currentHash, session.ContentHash, StringComparison.OrdinalIgnoreCase))
            { await uow.RollbackAsync(); await sessions.IncrementFailureAsync(tenantId, command.SessionId, "document_changed", ct); return new CompleteSignatureResult(false, null, SignatureValidationStatus.DOCUMENT_CHANGED, SignatureValidationStatus.NOT_VERIFIABLE, SignatureValidationStatus.NOT_VERIFIABLE, SignatureValidationStatus.INVALID, SignatureConformityStatus.NOT_EVALUATED, "document_changed"); }
            doc.Position = 0;
            var report = await validation.ValidateDetachedAsync(doc, command.Cms, command.Certificate, ct);
            var checks = EnsureMandatoryChecks(report.Checks);
            var outcome = outcomeFactory.Create(checks);
            var signatureId = Guid.NewGuid();
            var sig = new DocumentSignatureRecord(signatureId, tenantId, command.SessionId, session.DocumentId, session.DocumentVersionId, "CMS_DETACHED", "CMS_PKCS7_DETACHED", "UNKNOWN", "LOCAL_AGENT", outcome.CryptographicStatus.ToString(), outcome.ValidationStatus.ToString(), outcome.ConformityStatus.ToString(), payloadHash, session.ContentHash, command.Cms, command.Certificate, command.CertificateChain, report.EngineVersion, session.CorrelationId, DateTimeOffset.UtcNow, outcome.CertificateStatus.ToString(), outcome.TrustStatus.ToString());
            var storedId = await CreateSignatureAsync(tx, sig);
            var run = new SignatureValidationRunRecord(Guid.NewGuid(), tenantId, storedId, outcome.CryptographicStatus.ToString(), outcome.ValidationStatus.ToString(), outcome.ConformityStatus.ToString(), report.EngineVersion, DateTimeOffset.UtcNow, session.CorrelationId, outcome.CertificateStatus.ToString(), outcome.TrustStatus.ToString());
            await CreateValidationRunAsync(tx, run);
            await StoreChecksAsync(tx, tenantId, run.Id, outcome.Checks);
            await StoreChainAsync(tx, tenantId, run.Id, command.CertificateChain);
            await StoreEvidenceAsync(tx, new SignatureEvidence(tenantId, storedId, "CMS_DETACHED_DER", "SHA-256", payloadHash, command.Cms, DateTimeOffset.UtcNow, session.CorrelationId));
            await RegisterEventAsync(tx, new SignatureEventRecord(Guid.NewGuid(), tenantId, command.SessionId, storedId, "CMS_COMPLETED", "Assinatura CMS destacada validada e persistida atomicamente.", session.CorrelationId, DateTimeOffset.UtcNow));
            await CompleteSessionAsync(tx, tenantId, command.SessionId, storedId);
            await uow.CommitAsync();
            return new CompleteSignatureResult(true, storedId, outcome.CryptographicStatus, outcome.CertificateStatus, outcome.TrustStatus, outcome.ValidationStatus, outcome.ConformityStatus, null);
        }
        catch (Exception ex) when (ex is CryptographicException or InvalidOperationException or IOException)
        {
            await uow.RollbackAsync();
            await sessions.IncrementFailureAsync(tenantId, command.SessionId, "signature_completion_failed", ct);
            return new CompleteSignatureResult(false, null, SignatureValidationStatus.SIGNATURE_CORRUPTED, SignatureValidationStatus.NOT_VERIFIABLE, SignatureValidationStatus.NOT_VERIFIABLE, SignatureValidationStatus.INVALID, SignatureConformityStatus.NOT_EVALUATED, "signature_completion_failed");
        }
    }


    private static async Task<SigningSessionRecord?> LockSessionForCompletionAsync(SigningTransactionContext tx, Guid tenantId, Guid sessionId) =>
        await tx.Connection.QuerySingleOrDefaultAsync<SigningSessionRecord>(new CommandDefinition(PostgresSigningSessionRepository.SelectForUpdateSql, new { tenantId, sessionId }, tx.Transaction, cancellationToken: tx.CancellationToken));
    private static async Task<DocumentSignatureRecord?> FindExistingCompletionAsync(SigningTransactionContext tx, Guid tenantId, Guid sessionId, string idempotencyKey, string payloadHash) =>
        await tx.Connection.QuerySingleOrDefaultAsync<DocumentSignatureRecord>(new CommandDefinition(PostgresSigningSessionRepository.ExistingCompletionSql, new { tenantId, sessionId, idempotencyKey, payloadHash }, tx.Transaction, cancellationToken: tx.CancellationToken));
    private static async Task<bool> ConsumeCompletionTokenAsync(SigningTransactionContext tx, Guid tenantId, Guid sessionId, string completionTokenHash, string idempotencyKey, string payloadHash) =>
        await tx.Connection.ExecuteScalarAsync<int>(new CommandDefinition("update ged.signing_session set status='VALIDATING', completion_token_consumed_at=coalesce(completion_token_consumed_at,now()), completion_idempotency_key=@idempotencyKey, idempotency_payload_hash=@payloadHash where tenant_id=@tenantId and id=@sessionId and status in ('CONTENT_ACCESSED','WAITING_CONFIRMATION','SIGNING') and expires_at>now() and completion_token_hash=@completionTokenHash and (completion_idempotency_key is null or (completion_idempotency_key=@idempotencyKey and idempotency_payload_hash=@payloadHash)) returning 1", new{tenantId,sessionId,completionTokenHash,idempotencyKey,payloadHash}, tx.Transaction, cancellationToken:tx.CancellationToken))==1;
    private static async Task<Guid> CreateSignatureAsync(SigningTransactionContext tx, DocumentSignatureRecord s) =>
        await tx.Connection.ExecuteScalarAsync<Guid>(new CommandDefinition("insert into ged.document_signature (id,tenant_id,signing_session_id,document_id,document_version_id,signature_type,signature_format,signature_profile,signature_source,cryptographic_status,certificate_status,trust_status,validation_status,conformity_status,cms_bytes,cms_sha256,content_sha256,certificate_der,certificate_chain_der,engine_version,correlation_id,created_at) values (@Id,@TenantId,@SessionId,@DocumentId,@DocumentVersionId,@SignatureType,@SignatureFormat,@SignatureProfile,@SignatureSource,@CryptographicStatus,@CertificateStatus,@TrustStatus,@ValidationStatus,@ConformityStatus,@CmsBytes,@CmsSha256,@ContentSha256,@CertificateDer,@CertificateChainDer,@EngineVersion,@CorrelationId,@CreatedAt) on conflict (tenant_id, signing_session_id) do update set signing_session_id=excluded.signing_session_id returning id", new { s.Id,s.TenantId,s.SessionId,s.DocumentId,s.DocumentVersionId,s.SignatureType,s.SignatureFormat,s.SignatureProfile,s.SignatureSource,s.CryptographicStatus,s.CertificateStatus,s.TrustStatus,s.ValidationStatus,s.ConformityStatus,s.CmsBytes,s.CmsSha256,s.ContentSha256,s.CertificateDer,CertificateChainDer=s.CertificateChainDer.ToArray(),s.EngineVersion,s.CorrelationId,s.CreatedAt }, tx.Transaction, cancellationToken:tx.CancellationToken));
    private static Task CreateValidationRunAsync(SigningTransactionContext tx, SignatureValidationRunRecord r) => tx.Connection.ExecuteAsync(new CommandDefinition("insert into ged.signature_validation_run(id,tenant_id,signature_id,cryptographic_status,certificate_status,trust_status,validation_status,conformity_status,engine_version,validated_at,correlation_id) values(@Id,@TenantId,@SignatureId,@CryptographicStatus,@CertificateStatus,@TrustStatus,@ValidationStatus,@ConformityStatus,@EngineVersion,@ValidatedAt,@CorrelationId) on conflict (id) do nothing", r, tx.Transaction, cancellationToken:tx.CancellationToken));
    private static async Task StoreChecksAsync(SigningTransactionContext tx, Guid tenantId, Guid validationRunId, IReadOnlyList<SignatureValidationCheck> checks){ var order=0; foreach(var c in checks) await tx.Connection.ExecuteAsync(new CommandDefinition("insert into ged.signature_validation_check(id,tenant_id,validation_run_id,name,status,message,evidence_hash,check_order,created_at) values(gen_random_uuid(),@tenantId,@validationRunId,@Name,@Status,@Message,@EvidenceHash,@order,now()) on conflict do nothing", new{tenantId,validationRunId,c.Name,Status=c.Status.ToString(),c.Message,c.EvidenceHash,order=order++}, tx.Transaction, cancellationToken:tx.CancellationToken)); }
    private static async Task StoreChainAsync(SigningTransactionContext tx, Guid tenantId, Guid validationRunId, IReadOnlyList<byte[]> chainDer){ for(var i=0;i<chainDer.Count;i++){ var hash=Convert.ToHexString(SHA256.HashData(chainDer[i])).ToLowerInvariant(); await tx.Connection.ExecuteAsync(new CommandDefinition("insert into ged.signature_certificate_chain(id,tenant_id,validation_run_id,chain_order,certificate_der,certificate_sha256,created_at) values(gen_random_uuid(),@tenantId,@validationRunId,@i,@der,@hash,now()) on conflict do nothing", new{tenantId,validationRunId,i,der=chainDer[i],hash}, tx.Transaction, cancellationToken:tx.CancellationToken));}}
    private static Task StoreEvidenceAsync(SigningTransactionContext tx, SignatureEvidence e) => tx.Connection.ExecuteAsync(new CommandDefinition("insert into ged.signature_evidence (id, tenant_id, signature_id, evidence_type, hash_algorithm, evidence_hash, evidence_bytes, captured_at, correlation_id) values (gen_random_uuid(), @TenantId,@SignatureId,@EvidenceType,@HashAlgorithm,@EvidenceHash,@EvidenceBytes,@CapturedAt,@CorrelationId) on conflict (tenant_id,signature_id,evidence_type,evidence_hash) do nothing", e, tx.Transaction, cancellationToken:tx.CancellationToken));
    private static Task RegisterEventAsync(SigningTransactionContext tx, SignatureEventRecord e) => tx.Connection.ExecuteAsync(new CommandDefinition("insert into ged.signature_event(id,tenant_id,signing_session_id,signature_id,event_type,safe_message,correlation_id,created_at) values(@Id,@TenantId,@SessionId,@SignatureId,@EventType,@SafeMessage,@CorrelationId,@CreatedAt) on conflict do nothing", e, tx.Transaction, cancellationToken:tx.CancellationToken));
    private static Task CompleteSessionAsync(SigningTransactionContext tx, Guid tenantId, Guid sessionId, Guid signatureId) => tx.Connection.ExecuteAsync(new CommandDefinition("update ged.signing_session set status='COMPLETED',completed_at=now(),signature_id=@signatureId where tenant_id=@tenantId and id=@sessionId and status='VALIDATING'", new{tenantId,sessionId,signatureId}, tx.Transaction, cancellationToken:tx.CancellationToken));
    private static IReadOnlyList<SignatureValidationCheck> EnsureMandatoryChecks(IReadOnlyList<SignatureValidationCheck> checks){ var list=checks.ToList(); foreach(var name in new[]{"CMS_STRUCTURE","CMS_DETACHED","SIGNER_COUNT","SIGNATURE_MATH","MESSAGE_DIGEST","CERTIFICATE_EMBEDDED","CERTIFICATE_MATCH","DIGEST_ALGORITHM","SIGNATURE_ALGORITHM","CERTIFICATE_VALIDITY","CERTIFICATE_KEY_USAGE","CERTIFICATE_EXTENDED_KEY_USAGE","CHAIN_BUILD","REVOCATION_NOT_EVALUATED","POLICY_NOT_EVALUATED","TIMESTAMP_NOT_PRESENT"}) if(!list.Any(c=>c.Name==name)) list.Add(new SignatureValidationCheck(name, name.EndsWith("NOT_EVALUATED")||name=="TIMESTAMP_NOT_PRESENT"?SignatureValidationStatus.INDETERMINATE:SignatureValidationStatus.NOT_VERIFIABLE, "Check registrado para homologação CMS RC3.")); return list; }
    public Task<SignatureValidationReport> ValidateAsync(ValidateSignatureCommand command, CancellationToken ct) => validation.ValidateAsync(command, ct);
    private string BuildContentUrl(Guid sessionId)
    {
        var baseUrl = options.Value.PublicServerBaseUrl?.TrimEnd();
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("DigitalSignature:PublicServerBaseUrl deve ser URL HTTPS absoluta.");
        return $"{uri.GetLeftPart(UriPartial.Authority)}/api/signing/sessions/{sessionId}/content";
    }
    private static string ComputeCompletionPayloadHash(CompleteSigningSessionCommand command)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        writer.Write(command.SessionId.ToByteArray()); writer.Write(command.TenantId.ToByteArray()); writer.Write(command.UserId.ToByteArray());
        WriteBytes(writer, command.Cms); WriteBytes(writer, command.Certificate);
        foreach (var item in command.CertificateChain.OrderBy(b => Convert.ToHexString(SHA256.HashData(b)), StringComparer.Ordinal)) WriteBytes(writer, item);
        writer.Write(command.AgentOperationId ?? string.Empty);
        writer.Write(command.AgentVersion ?? string.Empty);
        writer.Write("CMS_PKCS7_DETACHED");
        writer.Write("LOCAL_AGENT");
        writer.Flush(); return Convert.ToHexString(SHA256.HashData(ms.ToArray())).ToLowerInvariant();
    }
    private static string Token() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static void WriteBytes(BinaryWriter writer, byte[] value){ writer.Write(value.Length); writer.Write(value); }
}
