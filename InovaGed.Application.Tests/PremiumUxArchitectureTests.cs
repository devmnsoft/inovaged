namespace InovaGed.Application.Tests;

public sealed class PremiumUxArchitectureTests
{
    [Fact]
    [Trait("Category", "Architecture")]
    public void Document_badges_partial_is_typed_and_presentation_only()
    {
        var source = File.ReadAllText(Find("InovaGed.Web", "Views", "Ged", "_DocumentBadges.cshtml"));
        Assert.Contains("DocumentBadgesVM", source);
        Assert.DoesNotContain("@inject", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IConfiguration", source, StringComparison.Ordinal);
        foreach (var state in new[] { "Restrito", "Parcial", "OCR pendente", "OCR pronto", "Preview falhou", "Sem classificação", "Vencido", "Assinado" })
            Assert.Contains(state, source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void Administration_view_does_not_build_dynamic_collections()
    {
        var source = File.ReadAllText(Find("InovaGed.Web", "Views", "Administration", "Index.cshtml"));
        Assert.Contains("AdministrationDashboardVM", source);
        Assert.DoesNotContain("?? []", source, StringComparison.Ordinal);
        Assert.DoesNotContain("dynamic", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IConfiguration", source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void Tracked_active_configuration_has_no_password()
    {
        var source = File.ReadAllText(Find("InovaGed.Web", "appsettings.json"));
        Assert.DoesNotContain("Password=", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"DefaultConnection\": \"\"", source, StringComparison.Ordinal);
    }

    private static string Find(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(string.Join('/', parts));
    }
}
