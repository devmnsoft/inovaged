using System.ComponentModel.DataAnnotations;
using InovaGed.Application.Contracts.Fiscalization;

namespace InovaGed.Application.Contracts.FiscalPortal;

public static class FiscalPortalPublicationStatus { public const string Draft="DRAFT", Published="PUBLISHED", Viewed="VIEWED", Accepted="ACCEPTED", Rejected="REJECTED", Expired="EXPIRED", Revoked="REVOKED"; }
public static class FiscalPortalDecision { public const string Accept="ACCEPT", Reject="REJECT", RequestCorrection="REQUEST_CORRECTION"; }
public static class FiscalSignatureType { public const string Electronic="ELECTRONIC", IcpBrasil="ICP_BRASIL"; }
public static class FiscalPortalPermissions { public const string AdminView="fiscal.portal.admin.view", Publish="fiscal.portal.publish", Revoke="fiscal.portal.revoke", View="fiscal.portal.view", Accept="fiscal.portal.accept", Reject="fiscal.portal.reject", Sign="fiscal.portal.sign", Export="fiscal.portal.export"; }

public sealed record FiscalPortalPublicationCreateCommand(Guid TenantId,Guid FiscalizationPeriodId,string AccessMode,string? AccessToken,DateTimeOffset? ExpiresAt,Guid CreatedBy,string? FiscalName,string? FiscalEmail,string? Notes);
public sealed record FiscalPortalPublicationFilter(Guid? FiscalizationPeriodId=null,string? Status=null);
public sealed class FiscalPortalPublicationItem
{
    public FiscalPortalPublicationItem() { }
    public FiscalPortalPublicationItem(Guid Id, Guid TenantId, Guid FiscalizationPeriodId, string PublicationNumber, string AccessMode, string Status, DateTimeOffset? PublishedAt, DateTimeOffset? ExpiresAt, string? FiscalName, string? FiscalEmail, DateTimeOffset? ViewedAt, DateTimeOffset? DecidedAt)
    {
        this.Id = Id;
        this.TenantId = TenantId;
        this.FiscalizationPeriodId = FiscalizationPeriodId;
        this.PublicationNumber = PublicationNumber;
        this.AccessMode = AccessMode;
        this.Status = Status;
        this.PublishedAt = PublishedAt;
        this.ExpiresAt = ExpiresAt;
        this.FiscalName = FiscalName;
        this.FiscalEmail = FiscalEmail;
        this.ViewedAt = ViewedAt;
        this.DecidedAt = DecidedAt;
    }
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid FiscalizationPeriodId { get; set; }
    public string PublicationNumber { get; set; } = "";
    public string AccessMode { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? FiscalName { get; set; }
    public string? FiscalEmail { get; set; }
    public DateTimeOffset? ViewedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
}
public sealed class FiscalPortalEventItem
{
    public FiscalPortalEventItem() { }
    public FiscalPortalEventItem(Guid Id, string EventType, string EventTitle, string? EventDescription, string? FromStatus, string? ToStatus, string? PerformedName, DateTimeOffset PerformedAt, string? IpAddress)
    {
        this.Id = Id;
        this.EventType = EventType;
        this.EventTitle = EventTitle;
        this.EventDescription = EventDescription;
        this.FromStatus = FromStatus;
        this.ToStatus = ToStatus;
        this.PerformedName = PerformedName;
        this.PerformedAt = PerformedAt;
        this.IpAddress = IpAddress;
    }
    public Guid Id { get; set; }
    public string EventType { get; set; } = "";
    public string EventTitle { get; set; } = "";
    public string? EventDescription { get; set; }
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public string? PerformedName { get; set; }
    public DateTimeOffset PerformedAt { get; set; }
    public string? IpAddress { get; set; }
}
public sealed class FiscalAcceptanceSignatureDetails
{
    public FiscalAcceptanceSignatureDetails() { }
    public FiscalAcceptanceSignatureDetails(Guid Id, string SignatureType, string SignerName, string? SignerEmail, string? SignerDocument, DateTimeOffset SignedAt, string? IpAddress, string SignedHashSha256, string? ValidationStatus)
    {
        this.Id = Id;
        this.SignatureType = SignatureType;
        this.SignerName = SignerName;
        this.SignerEmail = SignerEmail;
        this.SignerDocument = SignerDocument;
        this.SignedAt = SignedAt;
        this.IpAddress = IpAddress;
        this.SignedHashSha256 = SignedHashSha256;
        this.ValidationStatus = ValidationStatus;
    }
    public Guid Id { get; set; }
    public string SignatureType { get; set; } = "";
    public string SignerName { get; set; } = "";
    public string? SignerEmail { get; set; }
    public string? SignerDocument { get; set; }
    public DateTimeOffset SignedAt { get; set; }
    public string? IpAddress { get; set; }
    public string SignedHashSha256 { get; set; } = "";
    public string? ValidationStatus { get; set; }
}
public sealed record FiscalPortalPublicationDetails(FiscalPortalPublicationItem Publication,ContractFiscalizationPeriodDetails Fiscalization,IReadOnlyList<FiscalPortalEventItem> Events,FiscalAcceptanceSignatureDetails? Signature);

public sealed record FiscalPortalManifestationCommand(Guid TenantId,Guid PublicationId,string ManifestationType,string Decision,string? Comments,Guid? SubmittedBy,string? SubmittedName,string? SubmittedEmail,string? IpAddress,string? UserAgent,string? RelatedItem=null);
public sealed record FiscalPortalAcceptCommand(Guid TenantId,Guid PublicationId,Guid? UserId,string Name,string? Email,string? Comments,string? IpAddress,string? UserAgent);
public sealed record FiscalPortalRejectCommand(Guid TenantId,Guid PublicationId,Guid? UserId,[Required]string Reason,[Required]string Details,string? RelatedItem,string? Name,string? Email,string? IpAddress,string? UserAgent);
public sealed record FiscalPortalCorrectionRequestCommand(Guid TenantId,Guid PublicationId,Guid? UserId,[Required]string Reason,[Required]string Details,string? RelatedItem,string? Name,string? Email,string? IpAddress,string? UserAgent);
public sealed record FiscalElectronicSignatureCommand(Guid TenantId,Guid PublicationId,Guid? UserId,[Required]string SignerName,string? SignerEmail,string? SignerDocument,[Required]bool DeclarationAccepted,string? Notes,string? IpAddress,string? UserAgent);
public sealed record FiscalIcpSignatureCommand(Guid TenantId,Guid PublicationId,Guid? UserId,string SignerName,string? SignerEmail,string SignedHashSha256,string SignedPayloadJson,string? CertificateSubject,string? CertificateSerialNumber,string? CertificateIssuer,DateTimeOffset? CertificateValidFrom,DateTimeOffset? CertificateValidTo,string ValidationStatus,string? IpAddress,string? UserAgent);

public interface IFiscalPortalPublicationService { Task<Guid>CreatePublicationAsync(FiscalPortalPublicationCreateCommand command,CancellationToken ct);Task PublishAsync(Guid tenantId,Guid publicationId,Guid userId,CancellationToken ct);Task RevokeAsync(Guid tenantId,Guid publicationId,Guid userId,string reason,CancellationToken ct);Task<FiscalPortalPublicationDetails?>GetAsync(Guid tenantId,Guid publicationId,CancellationToken ct);Task<FiscalPortalPublicationDetails?>GetByTokenAsync(string token,CancellationToken ct);Task<IReadOnlyList<FiscalPortalPublicationItem>>ListAsync(Guid tenantId,FiscalPortalPublicationFilter filter,CancellationToken ct);Task MarkViewedAsync(Guid tenantId,Guid publicationId,string?ip,string?userAgent,CancellationToken ct); }
public interface IFiscalPortalManifestationService { Task<Guid>SubmitManifestationAsync(FiscalPortalManifestationCommand command,CancellationToken ct);Task AcceptAsync(FiscalPortalAcceptCommand command,CancellationToken ct);Task RejectAsync(FiscalPortalRejectCommand command,CancellationToken ct);Task RequestCorrectionAsync(FiscalPortalCorrectionRequestCommand command,CancellationToken ct); }
public interface IFiscalAcceptanceSignatureService { Task<Guid>SignElectronicallyAsync(FiscalElectronicSignatureCommand command,CancellationToken ct);Task<Guid>RegisterIcpSignatureAsync(FiscalIcpSignatureCommand command,CancellationToken ct);Task<FiscalAcceptanceSignatureDetails?>GetSignatureAsync(Guid tenantId,Guid signatureId,CancellationToken ct);Task<string>GenerateAcceptanceHashAsync(Guid tenantId,Guid fiscalizationPeriodId,CancellationToken ct); }
public interface IFiscalPortalNotificationService { Task NotifyPublicationAsync(Guid tenantId,Guid publicationId,CancellationToken ct);Task NotifyAcceptanceAsync(Guid tenantId,Guid publicationId,CancellationToken ct);Task NotifyRejectionAsync(Guid tenantId,Guid publicationId,CancellationToken ct);Task NotifyCorrectionRequestAsync(Guid tenantId,Guid publicationId,CancellationToken ct); }
