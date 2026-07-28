using InovaGed.Application.Audit;

namespace InovaGed.Application.Auth;

public enum AuthenticationFailureReason
{
    UserNotFound,
    InvalidPassword,
    InvalidPasswordHash,
    UserInactive,
    UserLocked,
    PasswordHashMissing,
    AuthorizationLoadError,
    TenantNotFound,
    TenantInactive
}

public enum AuthenticationDenialReason
{
    NoAccessRole,
    MfaRequired,
    CertificateRequired
}

public enum AuthenticationOutcome { Success, Failure, Denied, Error, Challenge }

public static class AuthenticationAuditReasonCodes
{
    public const string UserNotFound = "USER_NOT_FOUND";
    public const string InvalidPassword = "INVALID_PASSWORD";
    public const string InvalidPasswordHash = "INVALID_PASSWORD_HASH";
    public const string UserInactive = "USER_INACTIVE";
    public const string UserLocked = "USER_LOCKED";
    public const string PasswordHashMissing = "PASSWORD_HASH_MISSING";
    public const string AuthorizationLoadError = "AUTHORIZATION_LOAD_ERROR";
    public const string NoAccessRole = "NO_ACCESS_ROLE";
    public const string TenantNotFound = "TENANT_NOT_FOUND";
    public const string TenantInactive = "TENANT_INACTIVE";
    public const string MfaRequired = "MFA_REQUIRED";
    public const string CertificateRequired = "CERTIFICATE_REQUIRED";
    public const string SessionRevoked = "SESSION_REVOKED";
}

public sealed record AuthenticationAuditContext(
    Guid TenantId, Guid? UserId, string? IpAddress, string? UserAgent,
    string CorrelationId, Guid? SessionId = null);

public sealed record AuthenticationSessionAuditContext(
    Guid TenantId, Guid UserId, Guid SessionId, string? IpAddress,
    string? UserAgent, string CorrelationId);

public interface IAuthenticationAuditService
{
    Task LoginSucceededAsync(AuthenticationAuditContext context, CancellationToken ct);
    Task LoginFailedAsync(AuthenticationAuditContext context, AuthenticationFailureReason reason, CancellationToken ct);
    Task LoginDeniedAsync(AuthenticationAuditContext context, AuthenticationDenialReason reason, CancellationToken ct);
    Task SessionCreatedAsync(AuthenticationSessionAuditContext context, CancellationToken ct);
    Task SessionRevokedAsync(AuthenticationSessionAuditContext context, CancellationToken ct);
}

public sealed class AuthenticationAuditService(IAuditWriter audit) : IAuthenticationAuditService
{
    public Task LoginSucceededAsync(AuthenticationAuditContext context, CancellationToken ct)
        => WriteAsync(context, "Login concluído.", "LOGIN_SUCCESS", "INFO", AuthenticationOutcome.Success, ct);

    public Task LoginFailedAsync(AuthenticationAuditContext context, AuthenticationFailureReason reason, CancellationToken ct)
    {
        var code = reason switch
        {
            AuthenticationFailureReason.UserNotFound => AuthenticationAuditReasonCodes.UserNotFound,
            AuthenticationFailureReason.InvalidPassword => AuthenticationAuditReasonCodes.InvalidPassword,
            AuthenticationFailureReason.InvalidPasswordHash => AuthenticationAuditReasonCodes.InvalidPasswordHash,
            AuthenticationFailureReason.UserInactive => AuthenticationAuditReasonCodes.UserInactive,
            AuthenticationFailureReason.UserLocked => AuthenticationAuditReasonCodes.UserLocked,
            AuthenticationFailureReason.PasswordHashMissing => AuthenticationAuditReasonCodes.PasswordHashMissing,
            AuthenticationFailureReason.AuthorizationLoadError => AuthenticationAuditReasonCodes.AuthorizationLoadError,
            AuthenticationFailureReason.TenantNotFound => AuthenticationAuditReasonCodes.TenantNotFound,
            _ => AuthenticationAuditReasonCodes.TenantInactive
        };
        var outcome = reason == AuthenticationFailureReason.AuthorizationLoadError ? AuthenticationOutcome.Error : AuthenticationOutcome.Failure;
        return WriteAsync(context, "Falha segura de autenticação.", code,
            outcome == AuthenticationOutcome.Error ? "ERROR" : "SECURITY", outcome, ct);
    }

    public Task LoginDeniedAsync(AuthenticationAuditContext context, AuthenticationDenialReason reason, CancellationToken ct)
    {
        var code = reason switch
        {
            AuthenticationDenialReason.NoAccessRole => AuthenticationAuditReasonCodes.NoAccessRole,
            AuthenticationDenialReason.MfaRequired => AuthenticationAuditReasonCodes.MfaRequired,
            _ => AuthenticationAuditReasonCodes.CertificateRequired
        };
        var outcome = reason == AuthenticationDenialReason.NoAccessRole ? AuthenticationOutcome.Denied : AuthenticationOutcome.Challenge;
        return WriteAsync(context, "Autenticação negada pela política de acesso.", code, "SECURITY", outcome, ct);
    }

    public Task SessionCreatedAsync(AuthenticationSessionAuditContext context, CancellationToken ct)
        => WriteAsync(ToContext(context), "Sessão de autenticação criada.", "SESSION_CREATED", "INFO", AuthenticationOutcome.Success, ct);

    public Task SessionRevokedAsync(AuthenticationSessionAuditContext context, CancellationToken ct)
        => WriteAsync(ToContext(context), "Sessão de autenticação revogada.", AuthenticationAuditReasonCodes.SessionRevoked, "SECURITY", AuthenticationOutcome.Success, ct);

    private async Task WriteAsync(AuthenticationAuditContext context, string summary, string reasonCode,
        string eventType, AuthenticationOutcome outcome, CancellationToken ct)
    {
        var result = await audit.WriteAsync(new AuditWriteCommand(
            context.TenantId, context.UserId, "LOGIN", "authentication", context.UserId,
            summary, context.IpAddress, context.UserAgent, context.CorrelationId,
            new { context.SessionId, occurredAtUtc = DateTimeOffset.UtcNow }, eventType,
            outcome.ToString().ToUpperInvariant(), reasonCode), ct);
        if (result.IsFailure)
            throw new InvalidOperationException($"Falha na auditoria de autenticação: {result.Error?.Code}");
    }

    private static AuthenticationAuditContext ToContext(AuthenticationSessionAuditContext context)
        => new(context.TenantId, context.UserId, context.IpAddress, context.UserAgent, context.CorrelationId, context.SessionId);
}
