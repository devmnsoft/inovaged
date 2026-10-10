using InovaGed.Web.Models.Ged;
using InovaGed.Web.Services;

namespace InovaGed.Application.Tests.Ged;

public sealed class GedPanelProtocolContractTests
{
    [Fact]
    public void Stale_panel_response_does_not_apply_to_another_document_or_closed_panel()
    {
        Assert.False(GedPanelResponseGate.Applies(1, 2, "A", "B", "v1", "v1", true));
        Assert.False(GedPanelResponseGate.Applies(1, 1, "A", "A", "v1", "v1", false));
        Assert.False(GedPanelResponseGate.Applies(1, 1, "A", "A", "historica", "atual", true));
        Assert.True(GedPanelResponseGate.Applies(4, 4, "A", "A", "v2", "v2", true));
        Assert.True(GedPanelResponseGate.Applies(5, 5, "A", "A", "", "", true));
        Assert.True(GedPanelResponseGate.Applies(5, 5, "A", "A", "", "resolvida", true));
        Assert.False(GedPanelResponseGate.Applies(5, 6, "A", "A", "", "resolvida", true));
    }

    [Fact]
    public void Protocol_origin_link_is_required_when_a_document_is_requested()
    {
        Assert.False(ProtocolOriginLinkRules.RequestsLink(null));
        Assert.False(ProtocolOriginLinkRules.RequestsLink(Guid.Empty));
        Assert.True(ProtocolOriginLinkRules.RequestsLink(Guid.NewGuid()));
        Assert.Equal(ProtocolOriginLinkRules.UnavailableDocument, ProtocolOriginLinkRules.BlockReason(false, true, true));
        Assert.Equal(ProtocolOriginLinkRules.EditRequired, ProtocolOriginLinkRules.BlockReason(true, true, false));
        Assert.Equal(ProtocolOriginLinkRules.EditRequired, ProtocolOriginLinkRules.BlockReason(true, false, true));
        Assert.Null(ProtocolOriginLinkRules.BlockReason(true, true, true));
    }

    [Fact]
    public void Unauthorized_protocol_link_is_omitted()
    {
        var hidden = DocumentProtocolLinkPresenter.Present(Guid.NewGuid(), "2026/1", "DOCUMENTO_GERAL", "observação interna", "Maria", false, "/Protocolo/Details/x");
        var visible = DocumentProtocolLinkPresenter.Present(Guid.NewGuid(), "2026/2", "DOCUMENTO_GERAL", "nota", "João", true, "/Protocolo/Details/y");
        Assert.Null(hidden);
        Assert.NotNull(visible);
        Assert.Equal("nota", visible!.Observacao);
        Assert.Equal("/Protocolo/Details/y", visible.DetailsUrl);
    }

    [Fact]
    public void Panel_script_discards_stale_responses_and_keeps_protocol_errors_distinct()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "InovaGed.sln"))) root = root.Parent;
        var js = File.ReadAllText(Path.Combine(root!.FullName, "InovaGed.Web/wwwroot/js/ged-document-side-panel.js"));
        var view = File.ReadAllText(Path.Combine(root.FullName, "InovaGed.Web/Views/Ged/_DocumentSidePanel.cshtml"));
        Assert.Contains("new AbortController()", js);
        Assert.Contains("GedPanelResponseGate", js);
        Assert.Contains("stillCurrent(request)", js);
        Assert.Contains("dataset.versionId = resolvedVersion", js);
        Assert.Contains("Sua sessão expirou", js);
        Assert.Contains("data-ged-protocols-retry", js);
        Assert.DoesNotContain("/Protocolo/Detalhes/", js);
        Assert.Contains("outcome === 'empty'", js);
        Assert.Contains("InovaGedSidePanel", js);
        Assert.DoesNotContain("Resolução CONARQ", view);
        Assert.DoesNotContain("0 anos", view);
        Assert.Contains("RetentionFinalDestinationCode", view);
        Assert.Contains("RetentionStartEventCode", view);
    }
}
