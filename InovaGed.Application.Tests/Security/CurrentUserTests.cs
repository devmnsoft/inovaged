using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using WebGed.WebApi.Security;

namespace InovaGed.Application.Tests.Security;

public sealed class CurrentUserTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void CurrentUser_ReadsValidClaims()
    {
        var currentUser = CreateCurrentUser(
            new Claim("tenant_id", TenantId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, UserId.ToString()),
            new Claim(ClaimTypes.Email, "user@example.com"),
            new Claim(ClaimTypes.Role, "Admin"));

        Assert.True(currentUser.IsAuthenticated);
        Assert.Equal(TenantId, currentUser.TenantId);
        Assert.Equal(UserId, currentUser.UserId);
        Assert.Equal("user@example.com", currentUser.Email);
        Assert.Contains("Admin", currentUser.Roles);
    }

    [Fact]
    public void CurrentUser_ThrowsWhenTenantClaimIsMissing()
    {
        var currentUser = CreateCurrentUser(new Claim(ClaimTypes.NameIdentifier, UserId.ToString()));

        Assert.Throws<UnauthorizedAccessException>(() => currentUser.TenantId);
    }

    [Fact]
    public void CurrentUser_ThrowsWhenUserClaimIsMissing()
    {
        var currentUser = CreateCurrentUser(new Claim("tenant_id", TenantId.ToString()));

        Assert.Throws<UnauthorizedAccessException>(() => currentUser.UserId);
    }

    [Fact]
    public void CurrentUser_ThrowsWhenGuidClaimIsInvalid()
    {
        var currentUser = CreateCurrentUser(
            new Claim("tenant_id", "not-a-guid"),
            new Claim(ClaimTypes.NameIdentifier, UserId.ToString()));

        Assert.Throws<UnauthorizedAccessException>(() => currentUser.TenantId);
    }

    private static CurrentUser CreateCurrentUser(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "Test");
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        return new CurrentUser(new HttpContextAccessor { HttpContext = context });
    }
}
