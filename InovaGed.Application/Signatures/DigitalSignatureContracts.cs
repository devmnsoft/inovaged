namespace InovaGed.Application.Signatures;

public enum SignatureType { INTERNAL, CMS_DETACHED, PADES, CADES, IMPORTED, REMOTE_PROVIDER }
public enum SigningProcessStatus { REQUESTED, WAITING_AGENT, CONTENT_ACCESSED, WAITING_CONFIRMATION, SIGNING, VALIDATING, COMPLETED, FAILED, CANCELLED, EXPIRED }
public enum SignatureValidationStatus { PENDING, VALID, INVALID, INDETERMINATE, NOT_VERIFIABLE, REVOKED, EXPIRED, NOT_YET_VALID, TRUSTED_TEST_CHAIN, TRUSTED_SYSTEM_CHAIN, UNTRUSTED_CHAIN, POLICY_INVALID, CPF_MISMATCH, CERTIFICATE_PURPOSE_INVALID, TIMESTAMP_INVALID, DOCUMENT_CHANGED, SIGNATURE_CORRUPTED, UNSUPPORTED_ALGORITHM, INTERNAL_ONLY }
public enum SignatureConformityStatus { NOT_EVALUATED, INDETERMINATE, NON_COMPLIANT, COMPLIANT }
public enum SignatureProfile { AD_RB, AD_RT, AD_RV, AD_RC, AD_RA, UNKNOWN, NOT_APPLICABLE }

public sealed record CreateSigningSessionRequest(Guid DocumentId, Guid DocumentVersionId, string? Purpose);
public sealed record CompleteSigningSessionRequest(string CompletionToken, string IdempotencyKey, string SignatureCmsBase64, string CertificateDerBase64, IReadOnlyList<string> CertificateChainDerBase64, string AgentOperationId, string AgentVersion);
public sealed record PrepareSignatureCommand(Guid TenantId, Guid UserId, Guid DocumentId, Guid DocumentVersionId, SignatureType Type, string Format, string? PolicyOid, string ContentHash, string ContentHashAlgorithm, string Nonce, DateTimeOffset ExpiresAt, string CorrelationId);
public sealed record PrepareSignatureResult(bool Success, Guid? SessionId, SigningProcessStatus Status, string? Nonce, DateTimeOffset? ExpiresAt, string? Error);
public sealed record CompleteSignatureCommand(Guid SessionId, byte[] Signature, byte[] PublicCertificateDer, IReadOnlyList<byte[]> CertificateChainDer, byte[]? TimestampToken, IReadOnlyDictionary<string, string> TechnicalMetadata);
public sealed record CompleteSignatureResult(bool Success, Guid? SignatureId, SignatureValidationStatus ValidationStatus, string? Error);
public sealed record ValidateSignatureCommand(Guid TenantId, Guid SignatureId, Guid DocumentId, Guid DocumentVersionId, byte[] ContentBytes, string CorrelationId);
public sealed record SignatureValidationReport(Guid ValidationRunId, SignatureValidationStatus Status, SignatureProfile Profile, DateTimeOffset ValidatedAt, string EngineVersion, IReadOnlyList<SignatureValidationCheck> Checks);
public sealed record SignatureValidationCheck(string Name, SignatureValidationStatus Status, string Message, string? EvidenceHash = null);

public interface ISigningOrchestrator
{
    Task<PrepareSignatureResult> PrepareAsync(PrepareSignatureCommand command, CancellationToken ct);
    Task<CompleteSignatureResult> CompleteAsync(CompleteSignatureCommand command, CancellationToken ct);
    Task<SignatureValidationReport> ValidateAsync(ValidateSignatureCommand command, CancellationToken ct);
}

public interface ISignatureGenerationProvider { SignatureType Type { get; } Task<CompleteSignatureResult> CompleteAsync(CompleteSignatureCommand command, CancellationToken ct); }
public interface ISignatureValidationService { Task<SignatureValidationReport> ValidateAsync(ValidateSignatureCommand command, CancellationToken ct); }
public interface ICertificateChainValidationService { Task<SignatureValidationCheck> ValidateChainAsync(byte[] signerCertificateDer, IReadOnlyList<byte[]> chainDer, DateTimeOffset validationTime, CancellationToken ct); }
public interface ICertificateIdentityService { CertificateIdentity Extract(byte[] certificateDer); }
public interface IRevocationValidationService { Task<SignatureValidationCheck> ValidateRevocationAsync(byte[] certificateDer, IReadOnlyList<byte[]> chainDer, CancellationToken ct); }
public interface ITimestampAuthorityClient { Task<TimestampTokenResult> RequestTimestampAsync(byte[] hash, string hashAlgorithm, string? policyOid, CancellationToken ct); }
public interface ISignaturePolicyCatalog { Task<SignaturePolicyDescriptor?> GetActiveForGenerationAsync(SignatureType type, SignatureProfile profile, CancellationToken ct); Task<SignaturePolicyDescriptor?> GetAcceptedForVerificationAsync(string oid, string? version, DateTimeOffset signingTime, CancellationToken ct); }
public interface ISignatureEvidenceRepository { Task StoreAsync(SignatureEvidence evidence, CancellationToken ct); }
public interface ISigningSessionRepository { Task SaveAsync(PrepareSignatureCommand command, PrepareSignatureResult result, CancellationToken ct); }
public interface ISignatureRepository { }
public interface ISignatureValidationRepository { }
public interface ISignatureEventRepository { }
public interface IDocumentVersionSigningContentService { }
public interface ISignaturePackageService { }
public interface ISigningJobRepository { Task SaveEventAsync(Guid jobId, string message, CancellationToken ct); }
public interface ISignedDocumentVersionService { Task<Guid> CreateSignedVersionAsync(Guid tenantId, Guid documentId, Guid sourceVersionId, byte[] signedBytes, string contentHash, CancellationToken ct); }
public interface IImportedSignatureDetector { Task<SignatureValidationReport> DetectAndValidateAsync(Guid tenantId, Guid documentId, Guid documentVersionId, byte[] contentBytes, CancellationToken ct); }
public interface ISignatureReportService { Task<SignatureValidationReport> RevalidateForReportAsync(ValidateSignatureCommand command, CancellationToken ct); }
public interface ISigningAgentChallengeService { Task<PrepareSignatureResult> CreateChallengeAsync(PrepareSignatureCommand command, CancellationToken ct); }
public interface IRemoteSigningProvider { Task<PrepareSignatureResult> StartAuthorizationAsync(PrepareSignatureCommand command, CancellationToken ct); Task<CompleteSignatureResult> CompleteAsync(Guid sessionId, CancellationToken ct); Task CancelAsync(Guid sessionId, CancellationToken ct); }

public sealed record CertificateIdentity(string? CommonName, string? MaskedCpf, string? CpfSearchHash, string? CnpjMasked, string? CertificateType, string? Subject, string? Issuer, string? SerialNumber, string? Thumbprint);
public sealed record TimestampTokenResult(bool Success, byte[]? Token, DateTimeOffset? TimestampTime, SignatureValidationStatus Status, string? Error);
public sealed record SignaturePolicyDescriptor(string Oid, string Name, SignatureType Type, SignatureProfile Profile, string Version, string Status, string? Hash, DateTimeOffset? ValidFrom, DateTimeOffset? ValidTo);
public sealed record SignatureEvidence(Guid TenantId, Guid SignatureId, string EvidenceType, string HashAlgorithm, string EvidenceHash, byte[]? EvidenceBytes, DateTimeOffset CapturedAt, string CorrelationId);
