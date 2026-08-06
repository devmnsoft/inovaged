using Xunit;

namespace InovaGed.Application.Tests;

public sealed class HospitalDocumentsPremiumUxContractTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    private static string Read(string relative) => File.ReadAllText(Path.Combine(Root, relative));

    [Fact]
    public void HospitalPreview_ExposesPremiumTabsAndActions()
    {
        var view = Read("InovaGed.Web/Views/HospitalDocuments/Index.cshtml");
        var css = Read("InovaGed.Web/wwwroot/css/hospital-documents.css");
        var js = Read("InovaGed.Web/wwwroot/js/hospital-documents.js");

        foreach (var tab in new[] { "Resumo", "Preview", "OCR", "Metadados", "Partes", "Histórico", "Ações" })
            Assert.Contains(tab, view);

        Assert.Contains("btnDownloadPreview", view);
        Assert.Contains("hospital-preview-action primary", view);
        Assert.Contains("renderPreviewSupportPanels", js);
        Assert.Contains("hospital-preview-panel.is-expanded", css);
        Assert.Contains("overflow-x:auto", css);
    }

    [Fact]
    public void GedUploadDropOverlay_IsContextualAndCompact()
    {
        var view = Read("InovaGed.Web/Views/Ged/Index.cshtml");
        var js = Read("InovaGed.Web/wwwroot/js/ged-drag-drop.js");
        var css = Read("InovaGed.Web/wwwroot/css/ged-explorer.css");

        Assert.Contains("data-upload-context=\"drag-files\"", view);
        Assert.Contains("upload-drop-atlas.svg", view);
        Assert.Contains("containsFiles(event)", js);
        Assert.Contains("ged-global-drop-overlay.is-visible:not([hidden])", css);
    }

    [Fact]
    public void FolderTree_UsesClearStateLanguage()
    {
        var css = Read("InovaGed.Web/wwwroot/css/ged-explorer.css");

        Assert.Contains("Folder tree clarity pass", css);
        Assert.Contains("ged-tree-node.open>.ged-tree-row .ged-tree-folder-open", css);
        Assert.Contains("ged-tree-row.active .ged-tree-folder", css);
        Assert.Contains("ged-folder-actions:hover", css);
    }

    [Fact]
    public void HospitalDocumentsSearch_HasShortCacheAndTimeoutForResponsiveness()
    {
        var controller = Read("InovaGed.Web/Controller/HospitalDocumentsController.cs");

        Assert.Contains("HospitalDocuments:Search:v2", controller);
        Assert.Contains("X-InovaGed-Cache", controller);
        Assert.Contains("commandTimeout: 12", controller);
        Assert.Contains("SlidingExpiration = TimeSpan.FromSeconds(20)", controller);
        Assert.DoesNotContain("SELECT vx.*", controller);
    }

    [Fact]
    public void GedPreview_HasSingleScrollSurfaceAndAccessibleFocusMode()
    {
        var js = Read("InovaGed.Web/wwwroot/js/ged-document-side-panel.js");
        var css = Read("InovaGed.Web/wwwroot/css/components/document-preview.css");

        Assert.Contains("panel.dataset.activeTab = tabName", js);
        Assert.Contains("aria-selected", js);
        Assert.Contains("e.key !== 'Escape'", js);
        Assert.Contains("[data-active-tab=\"preview\"] .ged-side-body", css);
        Assert.Contains("body.ged-reader-focus-open", css);
    }
}
