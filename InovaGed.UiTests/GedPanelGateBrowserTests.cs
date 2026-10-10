using Microsoft.Playwright;
using Xunit;

namespace InovaGed.UiTests;

public sealed class GedPanelGateBrowserTests
{
    [Fact]
    public async Task Chromium_runs_the_real_panel_gate_and_folder_refresh_scripts()
    {
        var root = FindRepoRoot();
        var gatePath = Path.Combine(root, "InovaGed.Web", "wwwroot", "js", "ged-panel-response-gate.js");
        var refreshPath = Path.Combine(root, "InovaGed.Web", "wwwroot", "js", "ged-upload-folder-refresh.js");
        Assert.True(File.Exists(gatePath));
        Assert.True(File.Exists(refreshPath));

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.SetContentAsync("<!DOCTYPE html><html><body><div id=\"gedDocumentSidePanel\" hidden></div></body></html>");
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Path = gatePath });
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Path = refreshPath });

        var ok = await page.EvaluateAsync<bool>(@"() => {
            const gate = window.GedPanelResponseGate;
            const refresh = window.GedUploadFolderRefresh;
            if (!gate || !refresh) return false;
            const captured = [{ documentId: 'doc-a' }];
            const emptyFinishFallsBack = refresh.resolveCreatedDocuments([], captured)[0].documentId === 'doc-a';
            const finishWins = refresh.resolveCreatedDocuments([{ documentId: 'doc-b' }], captured)[0].documentId === 'doc-b';
            const staleDocument = gate.applies({ generation: 1, documentId: 'A', versionId: 'v1' }, { open: true, generation: 1, documentId: 'B', versionId: 'v1' }) === false;
            const closedPanel = gate.applies({ generation: 1, documentId: 'A', versionId: 'v1' }, { open: false, generation: 1, documentId: 'A', versionId: 'v1' }) === false;
            const unresolvedVersion = gate.applies({ generation: 2, documentId: 'A', versionId: '' }, { open: true, generation: 2, documentId: 'A', versionId: 'resolved' }) === true;
            const staleGeneration = gate.applies({ generation: 1, documentId: 'A', versionId: '' }, { open: true, generation: 2, documentId: 'A', versionId: 'resolved' }) === false;
            const noticeSeparatesTransfer = refresh.transferSeparateNotice.includes('OCR') && refresh.transferSeparateNotice.includes('visualização');
            return emptyFinishFallsBack && finishWins && staleDocument && closedPanel && unresolvedVersion && staleGeneration && noticeSeparatesTransfer;
        }");

        Assert.True(ok);
        Assert.Empty(errors);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "InovaGed.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Raiz do repositório não encontrada.");
    }
}
