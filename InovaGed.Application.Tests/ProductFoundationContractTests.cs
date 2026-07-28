using System.Security.Claims;
using InovaGed.Web.Security;

namespace InovaGed.Application.Tests;

public sealed class ProductFoundationContractTests
{
    [Fact]
    [Trait("Category", "Architecture")]
    public void Administrator_home_is_the_operational_dashboard()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, AppRoles.Admin)], "test"));

        Assert.Equal("/GedDashboard", AppStartRouteResolver.AdminHome);
        Assert.Equal("/GedDashboard", AppStartRouteResolver.GetDefaultHome(principal));
    }

    [Theory]
    [Trait("Category", "Architecture")]
    [InlineData("Views/Shared/_Layout.cshtml")]
    [InlineData("Views/Shared/_LayoutAuth.cshtml")]
    public void Essential_layout_assets_are_local(string relativePath)
    {
        var path = FindRepositoryFile("InovaGed.Web", relativePath);
        var layout = File.ReadAllText(path);

        Assert.DoesNotContain("cdn.jsdelivr.net", layout, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", layout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("~/lib/bootstrap", layout, StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(string.Join('/', parts));
    }
}
