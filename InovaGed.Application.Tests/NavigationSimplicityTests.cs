using System.Text.RegularExpressions;

namespace InovaGed.Application.Tests;

public sealed class NavigationSimplicityTests
{
    [Fact]
    [Trait("Category", "VisualContract")]
    public void Administrator_navigation_has_exactly_six_primary_groups()
    {
        var menu = ClassicThemeContractTests.Read("InovaGed.Web/Views/Shared/_SidebarMenu.cshtml");
        var fullAdmin = menu[menu.IndexOf("@if (isFullAdmin)", StringComparison.Ordinal)..menu.IndexOf("else if (isAdministradorOphir)", StringComparison.Ordinal)];
        var groups = Regex.Matches(fullAdmin, "<summary>([^<]+)</summary>").Select(match => match.Groups[1].Value).ToArray();
        Assert.Equal(["Visão Geral", "Gestão Documental", "Arquivo Físico", "Atendimento", "Governança", "Administração"], groups);
        Assert.Equal(6, Regex.Matches(fullAdmin, "<details class=\"sidebar-group\"").Count);
    }
}
