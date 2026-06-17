using InovaGed.Domain.Primitives;

namespace InovaGed.Application.Ged.Loans;

public sealed class CreateSecureDocumentLinkRequest
{
    public Guid? LoanRequestId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid? VersionId { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? RecipientName { get; set; }
    public string? RecipientContact { get; set; }
    public bool IsPermanent { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public int? MaxAccessCount { get; set; }
    public bool AllowPreview { get; set; } = true;
    public bool AllowDownload { get; set; } = false;
    public bool AllowSmartSearch { get; set; } = true;
    public string? MessageToRequester { get; set; }
}

public sealed class SecureDocumentLinkResult
{
    public Guid LinkId { get; set; }
    public string PublicUrl { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public bool IsPermanent { get; set; }
    public int? MaxAccessCount { get; set; }
}

public sealed class SecureDocumentLinkValidationResult
{
    public bool IsValid { get; set; }
    public string? DeniedReason { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid LinkId { get; set; }
    public Guid TenantId { get; set; }
    public Guid? LoanRequestId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid? VersionId { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public bool IsPermanent { get; set; }
    public int? MaxAccessCount { get; set; }
    public int AccessCount { get; set; }
    public bool AllowPreview { get; set; }
    public bool AllowDownload { get; set; }
    public bool AllowSmartSearch { get; set; }
}

public sealed class SecureDocumentLinkRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? LoanRequestId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid? VersionId { get; set; }

    public string? PublicUrl { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? RecipientName { get; set; }
    public string? RecipientContact { get; set; }

    public bool IsPermanent { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public int? MaxAccessCount { get; set; }
    public int AccessCount { get; set; }

    public bool AllowPreview { get; set; }
    public bool AllowDownload { get; set; }
    public bool AllowSmartSearch { get; set; }

    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? RevokedBy { get; set; }
    public string? RevokeReason { get; set; }

    public bool IsRevoked => RevokedAt.HasValue;
    public bool IsExpired => !IsPermanent && ExpiresAtUtc.HasValue && ExpiresAtUtc.Value <= DateTimeOffset.UtcNow;
    public bool IsAccessLimitReached => MaxAccessCount.HasValue && AccessCount >= MaxAccessCount.Value;
    public bool IsActive => !IsRevoked && !IsExpired && !IsAccessLimitReached;
}

public interface ISecureDocumentLinkService
{
    Task<SecureDocumentLinkResult> CreateAsync(Guid tenantId, Guid userId, CreateSecureDocumentLinkRequest request, CancellationToken ct);
    Task<Result> RevokeAsync(Guid tenantId, Guid linkId, Guid userId, string? reason, CancellationToken ct);
    Task<SecureDocumentLinkValidationResult> ValidateTokenAsync(string token, string? ip, string? userAgent, bool countAccess, CancellationToken ct);
    Task<IReadOnlyList<SecureDocumentLinkRow>> ListByLoanAsync(Guid tenantId, Guid loanRequestId, CancellationToken ct);
    Task<IReadOnlyList<SecureDocumentLinkRow>> ListByDocumentAsync(Guid tenantId, Guid documentId, CancellationToken ct);
}
