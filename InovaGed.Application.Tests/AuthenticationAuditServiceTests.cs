using InovaGed.Application.Audit;
using InovaGed.Application.Auth;
using InovaGed.Domain.Primitives;

namespace InovaGed.Application.Tests;

public sealed class AuthenticationAuditServiceTests
{
    [Theory]
    [InlineData(AuthenticationFailureReason.AuthorizationLoadError, "ERROR", "ERROR", "AUTHORIZATION_LOAD_ERROR")]
    public async Task Login_failure_uses_typed_login_event(AuthenticationFailureReason reason, string outcome, string eventType, string reasonCode)
    {
        var writer = new CapturingAuditWriter();
        var service = new AuthenticationAuditService(writer);
        var userId = Guid.NewGuid();
        var correlationId = Guid.NewGuid().ToString("N");

        await service.LoginFailedAsync(new AuthenticationAuditContext(
            Guid.NewGuid(), userId, "127.0.0.1", "test", correlationId), reason, default);

        Assert.NotNull(writer.Command);
        Assert.Equal("LOGIN", writer.Command.Action);
        Assert.Equal(userId, writer.Command.EntityId);
        Assert.Equal(outcome, writer.Command.Outcome);
        Assert.Equal(eventType, writer.Command.EventType);
        Assert.Equal(reasonCode, writer.Command.ReasonCode);
        Assert.Equal(correlationId, writer.Command.CorrelationId);
    }

    [Fact]
    public async Task Login_without_role_is_denied_as_security_event()
    {
        var writer = new CapturingAuditWriter();
        var service = new AuthenticationAuditService(writer);
        await service.LoginDeniedAsync(new AuthenticationAuditContext(
            Guid.NewGuid(), Guid.NewGuid(), null, null, "correlation"),
            AuthenticationDenialReason.NoAccessRole, default);

        Assert.Equal("DENIED", writer.Command!.Outcome);
        Assert.Equal("SECURITY", writer.Command.EventType);
        Assert.Equal("NO_ACCESS_ROLE", writer.Command.ReasonCode);
    }

    private sealed class CapturingAuditWriter : IAuditWriter
    {
        public AuditWriteCommand? Command { get; private set; }
        public Task<Result> WriteAsync(AuditWriteCommand command, CancellationToken ct)
        {
            Command = command;
            return Task.FromResult(Result.Ok());
        }

#pragma warning disable CS0618
        public Task<Result> WriteAsync(Guid tenantId, Guid? userId, string action, string entityName,
            Guid? entityId, string? summary, string? ipAddress, string? userAgent, object? data,
            CancellationToken ct) => throw new NotSupportedException();
#pragma warning restore CS0618
    }
}
