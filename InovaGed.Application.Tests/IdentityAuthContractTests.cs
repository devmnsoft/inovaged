namespace InovaGed.Application.Tests;

public sealed class IdentityAuthContractTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    [Fact]
    [Trait("Category", "IdentityAuthIntegration")]
    public void Authentication_uses_the_canonical_user_role_relationship()
    {
        var repository = File.ReadAllText(Path.Combine(Root, "InovaGed.Infrastructure/Auth/AuthRepository.cs"));
        Assert.Contains("SELECT DISTINCT r.normalized_name", repository);
        Assert.Contains("r.tenant_id = u.tenant_id", repository);
        Assert.DoesNotContain("ur.tenant_id", repository);
        Assert.DoesNotContain("ur.is_active", repository);
        Assert.DoesNotContain("r.is_active", repository);
    }

    [Fact]
    [Trait("Category", "IdentityAuthIntegration")]
    public void Login_does_not_grant_a_default_role_or_treat_username_as_role()
    {
        var controller = File.ReadAllText(Path.Combine(Root, "InovaGed.Web/Controller/AccountController.cs"));
        Assert.DoesNotContain("normalizedRoles.Add(AppRoles.Operador)", controller);
        Assert.Contains("LOGIN_DENIED_NO_ROLE", controller);
        Assert.DoesNotContain("normalizedUsername", controller);
    }

    [Fact]
    [Trait("Category", "IdentityAuthIntegration")]
    public void Integrity_migration_contains_database_enforced_tenant_isolation()
    {
        var migration = File.ReadAllText(Path.Combine(Root, "database/migrations/2026_07_identity_role_integrity.sql"));
        Assert.Contains("enforce_user_role_same_tenant", migration);
        Assert.Contains("ux_user_role_user_role", migration);
        Assert.Contains("vw_user_role_effective", migration);
        Assert.Contains("RAISE EXCEPTION", migration);
    }
}
