namespace InovaGed.Application.Signatures;

public enum SignatureType { INTERNAL, CMS_DETACHED, PADES, CADES, IMPORTED, REMOTE_PROVIDER }
public enum SigningProcessStatus { REQUESTED, WAITING_AGENT, CONTENT_ACCESSED, WAITING_CONFIRMATION, SIGNING, VALIDATING, COMPLETED, FAILED, CANCELLED, EXPIRED }
public enum SignatureValidationStatus { PENDING, VALID, INVALID, INDETERMINATE, NOT_VERIFIABLE, REVOKED, EXPIRED, NOT_YET_VALID, TRUSTED_TEST_CHAIN, TRUSTED_SYSTEM_CHAIN, UNTRUSTED_CHAIN, POLICY_INVALID, CPF_MISMATCH, CERTIFICATE_PURPOSE_INVALID, TIMESTAMP_INVALID, DOCUMENT_CHANGED, SIGNATURE_CORRUPTED, UNSUPPORTED_ALGORITHM, INTERNAL_ONLY }
public enum SignatureConformityStatus { NOT_EVALUATED, INDETERMINATE, NON_COMPLIANT, COMPLIANT }
public enum SignatureProfile { AD_RB, AD_RT, AD_RV, AD_RC, AD_RA, UNKNOWN, NOT_APPLICABLE }

public sealed record CreateSigningSessionRequest(Guid DocumentId, Guid DocumentVersionId, string? Purpose);

public sealed record CreateSigningSessionResponse(Guid SessionId, string Status, string ContentUrl, string ContentToken, string CompletionToken, string ExpectedSha256, long SizeBytes, string FileName, string DocumentCode, string VersionLabel, DateTimeOffset ExpiresAt, string CorrelationId);
public sealed class SigningSessionRecord
{
    public SigningSessionRecord() { }
    public SigningSessionRecord(Guid Id, Guid TenantId, Guid UserId, Guid DocumentId, Guid DocumentVersionId, string Status, string ContentHash, string ContentHashAlgorithm, long SizeBytes, string FileName, string DocumentCode, string VersionLabel, DateTimeOffset ExpiresAt, DateTimeOffset? FirstContentAccessedAt, DateTimeOffset? CompletedAt, DateTimeOffset? CancelledAt, int FailureCount, string CorrelationId, Guid? SignatureId = null, string? SafeError = null)
    {
        this.Id = Id;
        this.TenantId = TenantId;
        this.UserId = UserId;
        this.DocumentId = DocumentId;
        this.DocumentVersionId = DocumentVersionId;
        this.Status = Status;
        this.ContentHash = ContentHash;
        this.ContentHashAlgorithm = ContentHashAlgorithm;
        this.SizeBytes = SizeBytes;
        this.FileName = FileName;
        this.DocumentCode = DocumentCode;
        this.VersionLabel = VersionLabel;
        this.ExpiresAt = ExpiresAt;
        this.FirstContentAccessedAt = FirstContentAccessedAt;
        this.CompletedAt = CompletedAt;
        this.CancelledAt = CancelledAt;
        this.FailureCount = FailureCount;
        this.CorrelationId = CorrelationId;
        this.SignatureId = SignatureId;
        this.SafeError = SafeError;
    }
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid DocumentVersionId { get; set; }
    public string Status { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string ContentHashAlgorithm { get; set; } = "";
    public long SizeBytes { get; set; }
    public string FileName { get; set; } = "";
    public string DocumentCode { get; set; } = "";
    public string VersionLabel { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? FirstContentAccessedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public int FailureCount { get; set; }
    public string CorrelationId { get; set; } = "";
    public Guid? SignatureId { get; set; } = null;
    public string? SafeError { get; set; } = null;
}
public sealed class DocumentSignatureRecord
{
    public DocumentSignatureRecord() { }
    public DocumentSignatureRecord(Guid Id, Guid TenantId, Guid SessionId, Guid DocumentId, Guid DocumentVersionId, string SignatureType, string SignatureFormat, string SignatureProfile, string SignatureSource, string CryptographicStatus, string ValidationStatus, string ConformityStatus, string CmsSha256, string ContentSha256, byte[] CmsBytes, byte[] CertificateDer, IReadOnlyList<byte[]> CertificateChainDer, string EngineVersion, string CorrelationId, DateTimeOffset CreatedAt, string CertificateStatus = "NOT_VERIFIABLE", string TrustStatus = "NOT_VERIFIABLE")
    {
        this.Id = Id;
        this.TenantId = TenantId;
        this.SessionId = SessionId;
        this.DocumentId = DocumentId;
        this.DocumentVersionId = DocumentVersionId;
        this.SignatureType = SignatureType;
        this.SignatureFormat = SignatureFormat;
        this.SignatureProfile = SignatureProfile;
        this.SignatureSource = SignatureSource;
        this.CryptographicStatus = CryptographicStatus;
        this.ValidationStatus = ValidationStatus;
        this.ConformityStatus = ConformityStatus;
        this.CmsSha256 = CmsSha256;
        this.ContentSha256 = ContentSha256;
        this.CmsBytes = CmsBytes;
        this.CertificateDer = CertificateDer;
        this.CertificateChainDer = CertificateChainDer;
        this.EngineVersion = EngineVersion;
        this.CorrelationId = CorrelationId;
        this.CreatedAt = CreatedAt;
        this.CertificateStatus = CertificateStatus;
        this.TrustStatus = TrustStatus;
    }
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SessionId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid DocumentVersionId { get; set; }
    public string SignatureType { get; set; } = "";
    public string SignatureFormat { get; set; } = "";
    public string SignatureProfile { get; set; } = "";
    public string SignatureSource { get; set; } = "";
    public string CryptographicStatus { get; set; } = "";
    public string ValidationStatus { get; set; } = "";
    public string ConformityStatus { get; set; } = "";
    public string CmsSha256 { get; set; } = "";
    public string ContentSha256 { get; set; } = "";
    public byte[] CmsBytes { get; set; } = null!;
    public byte[] CertificateDer { get; set; } = null!;
    public IReadOnlyList<byte[]> CertificateChainDer { get; set; } = null!;
    public string EngineVersion { get; set; } = "";
    public string CorrelationId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string CertificateStatus { get; set; } = "NOT_VERIFIABLE";
    public string TrustStatus { get; set; } = "NOT_VERIFIABLE";
}
public sealed class SignatureValidationRunRecord
{
    public SignatureValidationRunRecord() { }
    public SignatureValidationRunRecord(Guid Id, Guid TenantId, Guid SignatureId, string CryptographicStatus, string ValidationStatus, string ConformityStatus, string EngineVersion, DateTimeOffset ValidatedAt, string CorrelationId, string CertificateStatus = "NOT_VERIFIABLE", string TrustStatus = "NOT_VERIFIABLE")
    {
        this.Id = Id;
        this.TenantId = TenantId;
        this.SignatureId = SignatureId;
        this.CryptographicStatus = CryptographicStatus;
        this.ValidationStatus = ValidationStatus;
        this.ConformityStatus = ConformityStatus;
        this.EngineVersion = EngineVersion;
        this.ValidatedAt = ValidatedAt;
        this.CorrelationId = CorrelationId;
        this.CertificateStatus = CertificateStatus;
        this.TrustStatus = TrustStatus;
    }
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SignatureId { get; set; }
    public string CryptographicStatus { get; set; } = "";
    public string ValidationStatus { get; set; } = "";
    public string ConformityStatus { get; set; } = "";
    public string EngineVersion { get; set; } = "";
    public DateTimeOffset ValidatedAt { get; set; }
    public string CorrelationId { get; set; } = "";
    public string CertificateStatus { get; set; } = "NOT_VERIFIABLE";
    public string TrustStatus { get; set; } = "NOT_VERIFIABLE";
}
public sealed class SignatureEventRecord
{
    public SignatureEventRecord() { }
    public SignatureEventRecord(Guid Id, Guid TenantId, Guid? SessionId, Guid? SignatureId, string EventType, string SafeMessage, string CorrelationId, DateTimeOffset CreatedAt)
    {
        this.Id = Id;
        this.TenantId = TenantId;
        this.SessionId = SessionId;
        this.SignatureId = SignatureId;
        this.EventType = EventType;
        this.SafeMessage = SafeMessage;
        this.CorrelationId = CorrelationId;
        this.CreatedAt = CreatedAt;
    }
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? SignatureId { get; set; }
    public string EventType { get; set; } = "";
    public string SafeMessage { get; set; } = "";
    public string CorrelationId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
public sealed class SigningContentMetadata
{
    public SigningContentMetadata() { }
    public SigningContentMetadata(Guid TenantId, Guid DocumentId, Guid DocumentVersionId, string FileName, string DocumentCode, string VersionLabel, string ContentType, long SizeBytes)
    {
        this.TenantId = TenantId;
        this.DocumentId = DocumentId;
        this.DocumentVersionId = DocumentVersionId;
        this.FileName = FileName;
        this.DocumentCode = DocumentCode;
        this.VersionLabel = VersionLabel;
        this.ContentType = ContentType;
        this.SizeBytes = SizeBytes;
    }
    public Guid TenantId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid DocumentVersionId { get; set; }
    public string FileName { get; set; } = "";
    public string DocumentCode { get; set; } = "";
    public string VersionLabel { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
}
public sealed record SignaturePackageFile(string FileName, string ContentType, Stream Content, string Sha256);
public sealed record CompleteSigningSessionRequest(string CompletionToken, string IdempotencyKey, string SignatureCmsBase64, string CertificateDerBase64, IReadOnlyList<string> CertificateChainDerBase64, string AgentOperationId, string AgentVersion);
public sealed record PrepareSignatureCommand(Guid TenantId, Guid UserId, Guid DocumentId, Guid DocumentVersionId, SignatureType Type, string Format, string? PolicyOid, string ContentHash, string ContentHashAlgorithm, string Nonce, DateTimeOffset ExpiresAt, string CorrelationId);
public sealed record PrepareSignatureResult(bool Success, Guid? SessionId, SigningProcessStatus Status, string? Nonce, DateTimeOffset? ExpiresAt, string? Error);
[Obsolete("Use CompleteSigningSessionCommand for CMS local-agent completion. Kept only for legacy providers.")]
public sealed record CompleteSignatureCommand(Guid SessionId, byte[] Signature, byte[] PublicCertificateDer, IReadOnlyList<byte[]> CertificateChainDer, byte[]? TimestampToken, IReadOnlyDictionary<string, string> TechnicalMetadata);
public sealed record PrepareSigningSessionCommand(Guid TenantId, Guid UserId, Guid DocumentId, Guid DocumentVersionId, string? Purpose, string CorrelationId, string RequestIpHash, string UserAgentHash);
public sealed record CompleteSigningSessionCommand(Guid TenantId, Guid UserId, Guid SessionId, string CompletionToken, string IdempotencyKey, byte[] Cms, byte[] Certificate, IReadOnlyList<byte[]> CertificateChain, string AgentOperationId, string AgentVersion, string CorrelationId);
public sealed record SignatureValidationOutcome(SignatureValidationStatus CryptographicStatus, SignatureValidationStatus CertificateStatus, SignatureValidationStatus TrustStatus, SignatureValidationStatus ValidationStatus, SignatureConformityStatus ConformityStatus, IReadOnlyList<SignatureValidationCheck> Checks);
public sealed record ContentCapabilityResult(Guid TenantId, Guid DocumentId, Guid DocumentVersionId, string FileName, string ContentType, long SizeBytes, string ExpectedSha256);

public sealed record CompleteSignatureResult(bool Success, Guid? SignatureId, SignatureValidationStatus CryptographicStatus, SignatureValidationStatus CertificateStatus, SignatureValidationStatus TrustStatus, SignatureValidationStatus ValidationStatus, SignatureConformityStatus ConformityStatus, string? Error)
{
    public CompleteSignatureResult(bool Success, Guid? SignatureId, SignatureValidationStatus ValidationStatus, string? Error) : this(Success, SignatureId, ValidationStatus, SignatureValidationStatus.INDETERMINATE, SignatureValidationStatus.NOT_VERIFIABLE, ValidationStatus, SignatureConformityStatus.NOT_EVALUATED, Error) { }
    public CompleteSignatureResult(bool Success, Guid? SignatureId, SignatureValidationStatus CryptographicStatus, SignatureValidationStatus CertificateStatus, SignatureValidationStatus ValidationStatus, SignatureConformityStatus ConformityStatus, string? Error) : this(Success, SignatureId, CryptographicStatus, CertificateStatus, SignatureValidationStatus.NOT_VERIFIABLE, ValidationStatus, ConformityStatus, Error) { }
}
public sealed record ValidateSignatureCommand(Guid TenantId, Guid SignatureId, Guid DocumentId, Guid DocumentVersionId, byte[] ContentBytes, string CorrelationId);
public sealed record SignatureValidationReport(Guid ValidationRunId, SignatureValidationStatus Status, SignatureProfile Profile, DateTimeOffset ValidatedAt, string EngineVersion, IReadOnlyList<SignatureValidationCheck> Checks);
public sealed record SignatureValidationCheck(string Name, SignatureValidationStatus Status, string Message, string? EvidenceHash = null);

public interface ISigningOrchestrator
{
    Task<CreateSigningSessionResponse> PrepareAsync(PrepareSigningSessionCommand command, CancellationToken ct);
    Task<CompleteSignatureResult> CompleteAsync(CompleteSigningSessionCommand command, CancellationToken ct);
    Task<SignatureValidationReport> ValidateAsync(ValidateSignatureCommand command, CancellationToken ct);
}

public interface ISignatureGenerationProvider { SignatureType Type { get; } Task<CompleteSignatureResult> CompleteAsync(CompleteSignatureCommand command, CancellationToken ct); }
public interface ISignatureValidationService { Task<SignatureValidationReport> ValidateAsync(ValidateSignatureCommand command, CancellationToken ct); Task<SignatureValidationReport> ValidateDetachedAsync(Stream content, ReadOnlyMemory<byte> cms, ReadOnlyMemory<byte>? expectedCertificate, CancellationToken ct); }
public interface ICertificateChainValidationService { Task<SignatureValidationCheck> ValidateChainAsync(byte[] signerCertificateDer, IReadOnlyList<byte[]> chainDer, DateTimeOffset validationTime, CancellationToken ct); }
public interface ICertificateIdentityService { CertificateIdentity Extract(byte[] certificateDer); }
public interface IRevocationValidationService { Task<SignatureValidationCheck> ValidateRevocationAsync(byte[] certificateDer, IReadOnlyList<byte[]> chainDer, CancellationToken ct); }
public interface ITimestampAuthorityClient { Task<TimestampTokenResult> RequestTimestampAsync(byte[] hash, string hashAlgorithm, string? policyOid, CancellationToken ct); }
public interface ISignaturePolicyCatalog { Task<SignaturePolicyDescriptor?> GetActiveForGenerationAsync(SignatureType type, SignatureProfile profile, CancellationToken ct); Task<SignaturePolicyDescriptor?> GetAcceptedForVerificationAsync(string oid, string? version, DateTimeOffset signingTime, CancellationToken ct); }
public interface ISignatureEvidenceRepository { Task StoreAsync(SignatureEvidence evidence, CancellationToken ct); }
public interface ISigningSessionRepository
{
    Task CreateAsync(SigningSessionRecord session, string contentTokenHash, string completionTokenHash, string nonceHash, CancellationToken ct);
    Task<SigningSessionRecord?> GetAsync(Guid tenantId, Guid sessionId, CancellationToken ct);
    Task<SigningSessionRecord?> GetForContentAsync(Guid tenantId, Guid sessionId, string contentTokenHash, CancellationToken ct);
    Task<ContentCapabilityResult?> ResolveAndConsumeContentCapabilityAsync(Guid sessionId, string contentTokenHash, CancellationToken ct);
    Task<SigningSessionRecord?> ResolveContentCapabilityAsync(Guid sessionId, string contentTokenHash, CancellationToken ct);
    Task<SigningSessionRecord?> ConsumeContentCapabilityAsync(Guid sessionId, string contentTokenHash, CancellationToken ct);
    Task<bool> ConsumeContentTokenAsync(Guid tenantId, Guid sessionId, string contentTokenHash, CancellationToken ct);
    Task<SigningSessionRecord?> GetForCompletionAsync(Guid tenantId, Guid sessionId, string completionTokenHash, CancellationToken ct);
    Task<bool> ConsumeCompletionTokenAsync(Guid tenantId, Guid sessionId, string completionTokenHash, string idempotencyKey, string payloadHash, CancellationToken ct);
    Task MarkContentAccessedAsync(Guid tenantId, Guid sessionId, CancellationToken ct);
    Task MarkWaitingConfirmationAsync(Guid tenantId, Guid sessionId, CancellationToken ct);
    Task MarkSigningAsync(Guid tenantId, Guid sessionId, CancellationToken ct);
    Task CompleteAsync(Guid tenantId, Guid sessionId, Guid signatureId, CancellationToken ct);
    Task<bool> CancelAsync(Guid tenantId, Guid sessionId, Guid userId, CancellationToken ct);
    Task<int> ExpireAsync(Guid tenantId, DateTimeOffset now, CancellationToken ct);
    Task IncrementFailureAsync(Guid tenantId, Guid sessionId, string safeError, CancellationToken ct);
    Task<DocumentSignatureRecord?> GetExistingCompletionAsync(Guid tenantId, Guid sessionId, string idempotencyKey, string payloadHash, CancellationToken ct);
    Task SaveAsync(PrepareSignatureCommand command, PrepareSignatureResult result, CancellationToken ct);
}
public interface ISignatureRepository
{
    Task<Guid> CreateAsync(DocumentSignatureRecord signature, CancellationToken ct);
    Task<DocumentSignatureRecord?> GetAsync(Guid tenantId, Guid signatureId, CancellationToken ct);
    Task<IReadOnlyList<DocumentSignatureRecord>> ListByDocumentAsync(Guid tenantId, Guid documentId, CancellationToken ct);
    Task<IReadOnlyList<DocumentSignatureRecord>> ListByVersionAsync(Guid tenantId, Guid documentId, Guid documentVersionId, CancellationToken ct);
    Task<byte[]?> GetCmsBytesAsync(Guid tenantId, Guid signatureId, CancellationToken ct);
    Task<byte[]?> GetCertificateAsync(Guid tenantId, Guid signatureId, CancellationToken ct);
    Task<bool> ExistsForSessionAsync(Guid tenantId, Guid sessionId, CancellationToken ct);
}
public interface ISignatureValidationRepository
{
    Task<Guid> CreateRunAsync(SignatureValidationRunRecord run, CancellationToken ct);
    Task StoreChecksAsync(Guid tenantId, Guid validationRunId, IReadOnlyList<SignatureValidationCheck> checks, CancellationToken ct);
    Task StoreChainAsync(Guid tenantId, Guid validationRunId, IReadOnlyList<byte[]> chainDer, CancellationToken ct);
    Task<SignatureValidationRunRecord?> GetLatestAsync(Guid tenantId, Guid signatureId, CancellationToken ct);
    Task<IReadOnlyList<SignatureValidationRunRecord>> ListHistoryAsync(Guid tenantId, Guid signatureId, CancellationToken ct);
}
public interface ISignatureEventRepository
{
    Task RegisterAsync(SignatureEventRecord evt, CancellationToken ct);
    Task<IReadOnlyList<SignatureEventRecord>> ListBySessionAsync(Guid tenantId, Guid sessionId, CancellationToken ct);
    Task<IReadOnlyList<SignatureEventRecord>> ListBySignatureAsync(Guid tenantId, Guid signatureId, CancellationToken ct);
}
public interface IDocumentVersionSigningContentService
{
    Task<SigningContentMetadata?> LocateVersionAsync(Guid tenantId, Guid documentId, Guid documentVersionId, CancellationToken ct);
    Task<bool> ValidateTenantAsync(Guid tenantId, Guid documentId, Guid documentVersionId, CancellationToken ct);
    Task<SigningContentMetadata> GetMetadataAsync(Guid tenantId, Guid documentId, Guid documentVersionId, CancellationToken ct);
    Task<Stream> OpenReadAsync(Guid tenantId, Guid documentId, Guid documentVersionId, CancellationToken ct);
    Task<string> CalculateSha256Async(Stream content, CancellationToken ct);
    Task ValidateSizeAsync(long sizeBytes, CancellationToken ct);
    Task<bool> ConfirmDocumentVersionLinkAsync(Guid tenantId, Guid documentId, Guid documentVersionId, CancellationToken ct);
}
public interface ISignaturePackageService
{
    Task<SignaturePackageFile> GenerateP7sAsync(Guid tenantId, Guid signatureId, CancellationToken ct);
    Task<SignaturePackageFile> GenerateValidationReportJsonAsync(Guid tenantId, Guid signatureId, CancellationToken ct);
    Task<SignaturePackageFile> GenerateZipAsync(Guid tenantId, Guid signatureId, CancellationToken ct);
    Task<IReadOnlyDictionary<string,string>> CalculateChecksumsAsync(IReadOnlyList<SignaturePackageFile> files, CancellationToken ct);
}
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

public sealed record SignatureDetailsResponse(
    Guid Id,
    Guid TenantId,
    Guid DocumentId,
    Guid DocumentVersionId,
    string SignatureType,
    string SignatureFormat,
    string SignatureProfile,
    string SignatureSource,
    string CmsSha256,
    string ContentSha256,
    string? SignerMaskedDocument,
    string? Issuer,
    string? SerialAbbreviated,
    DateTimeOffset? CertificateNotBefore,
    DateTimeOffset? CertificateNotAfter,
    string CryptographicStatus,
    string CertificateStatus,
    string TrustStatus,
    string ValidationStatus,
    string ConformityStatus,
    string EngineVersion,
    DateTimeOffset CreatedAt,
    string CorrelationId);

public sealed record SignatureValidationDetailsResponse(
    Guid ValidationRunId,
    Guid SignatureId,
    string CryptographicStatus,
    string CertificateStatus,
    string TrustStatus,
    string ValidationStatus,
    string ConformityStatus,
    string EngineVersion,
    DateTimeOffset ValidatedAt,
    IReadOnlyList<SignatureValidationCheck> Checks,
    string CorrelationId);
