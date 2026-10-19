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
        Assert.Equal(["Visão Geral", "Gestão Documental", "Demais setores", "Arquivo Físico", "Atendimento", "Governança", "Administração"], groups);
        Assert.Equal(7, Regex.Matches(fullAdmin, "<details class=\"sidebar-group\"").Count);
        Assert.Contains("Mover documentos em lote", fullAdmin, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"AutoSchedule\"", fullAdmin, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"Cadastrar\"", fullAdmin, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"Consultar\"", fullAdmin, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"Parametrizar\"", fullAdmin, StringComparison.Ordinal);
        Assert.Contains("<details class=\"sidebar-group\" open>", fullAdmin, StringComparison.Ordinal);
        Assert.Contains("Checklist de faturamento", fullAdmin, StringComparison.Ordinal);
        Assert.Contains(">Faturamento<", fullAdmin, StringComparison.Ordinal);
        Assert.Contains(">Temporalidade<", fullAdmin, StringComparison.Ordinal);
    }
}
