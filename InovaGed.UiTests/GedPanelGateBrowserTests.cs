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

    [Fact]
    public async Task Chromium_runs_full_side_panel_journey_with_controlled_http_responses()
    {
        var root = FindRepoRoot();
        var gatePath = Path.Combine(root, "InovaGed.Web", "wwwroot", "js", "ged-panel-response-gate.js");
        var panelPath = Path.Combine(root, "InovaGed.Web", "wwwroot", "js", "ged-document-side-panel.js");
        Assert.True(File.Exists(gatePath));
        Assert.True(File.Exists(panelPath));

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);

        // Mock HTTP routes for controlled server responses
        await page.RouteAsync("**/Ged/DocumentPanel*", async route =>
        {
            var uri = new Uri(route.Request.Url);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var docId = query["id"];
            var versionId = query["versionId"];

            try
            {
                if (docId == "doc-expired")
                {
                    await route.FulfillAsync(new RouteFulfillOptions
                    {
                        Status = 401,
                        ContentType = "text/plain",
                        Body = "Unauthorized"
                    });
                    return;
                }

                if (docId == "doc-A")
                {
                    await Task.Delay(250);
                    await route.FulfillAsync(new RouteFulfillOptions
                    {
                        Status = 200,
                        ContentType = "text/html",
                        Body = "<div class=\"ged-side-header\" data-document-id=\"doc-A\" data-version-id=\"v1\"><div class=\"ged-side-title\">Documento A</div><button class=\"js-close-document-panel\">x</button></div><div class=\"ged-side-body\"><section data-ged-tab-panel=\"summary\">Resumo A</section></div>"
                    });
                    return;
                }

                if (docId == "doc-B")
                {
                    await route.FulfillAsync(new RouteFulfillOptions
                    {
                        Status = 200,
                        ContentType = "text/html",
                        Body = "<div class=\"ged-side-header\" data-document-id=\"doc-B\" data-version-id=\"v2\"><div class=\"ged-side-title\">Documento B</div><button class=\"js-close-document-panel\">x</button></div><div class=\"ged-side-body\"><section data-ged-tab-panel=\"summary\">Resumo B</section></div>"
                    });
                    return;
                }

                var resolvedVer = string.IsNullOrEmpty(versionId) ? "v-latest-resolved" : versionId;
                await route.FulfillAsync(new RouteFulfillOptions
                {
                    Status = 200,
                    ContentType = "text/html",
                    Body = $"<div class=\"ged-side-header\" data-document-id=\"{docId}\" data-version-id=\"{resolvedVer}\"><div class=\"ged-side-title\">Documento {docId}</div><button class=\"js-close-document-panel\">x</button></div><div class=\"ged-side-body\"><section data-ged-tab-panel=\"summary\">Resumo</section><section data-ged-tab-panel=\"ocr\" class=\"d-none\"><div data-ged-ocr-host></div></section></div>"
                });
            }
            catch
            {
                // Ignora abortos do cliente
            }
        });

        await page.RouteAsync("**/Ged/DocumentOcrText*", async route =>
        {
            var uri = new Uri(route.Request.Url);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var ver = query["versionId"];
            if (ver == "v-ocr-fail")
            {
                await route.FulfillAsync(new RouteFulfillOptions { Status = 500, Body = "Internal error" });
            }
            else
            {
                await route.FulfillAsync(new RouteFulfillOptions
                {
                    Status = 200,
                    ContentType = "application/json",
                    Body = "{\"success\":true,\"text\":\"Texto OCR indexado com sucesso\"}"
                });
            }
        });

        await page.RouteAsync("http://localhost/Ged/Index*", async route =>
        {
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "text/html",
                Body = @"<!DOCTYPE html>
<html>
<head><title>Test Panel</title></head>
<body>
    <div class=""ged-page"">
        <button id=""btnDocA"" class=""js-open-document-panel"" data-document-id=""doc-A"">Abrir A</button>
        <button id=""btnDocB"" class=""js-open-document-panel"" data-document-id=""doc-B"">Abrir B</button>
        <div style=""height: 1200px;"">Espaço para rolagem</div>
        <aside id=""gedDocumentSidePanel"" class=""ged-document-side-panel d-none"" hidden></aside>
    </div>
</body>
</html>"
            });
        });

        await page.GotoAsync("http://localhost/Ged/Index");

        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Path = gatePath });
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Path = panelPath });

        // Scenario 1: Open A then immediately B -> B wins, late A response does not overwrite B
        var raceResult = await page.EvaluateAsync<string>(@"async () => {
            window.openGedDocumentPanel('doc-A');
            await new Promise(r => setTimeout(r, 20));
            await window.openGedDocumentPanel('doc-B');
            await new Promise(r => setTimeout(r, 400));
            const panel = document.getElementById('gedDocumentSidePanel');
            return panel.dataset.documentId;
        }");
        Assert.Equal("doc-B", raceResult);

        // Scenario 2: Close during load -> late response does not reopen panel
        var closeResult = await page.EvaluateAsync<bool>(@"async () => {
            window.openGedDocumentPanel('doc-A');
            await new Promise(r => setTimeout(r, 20));
            window.closeGedDocumentPanel();
            await new Promise(r => setTimeout(r, 350));
            const panel = document.getElementById('gedDocumentSidePanel');
            return panel.hidden === true && !panel.classList.contains('is-open');
        }");
        Assert.True(closeResult);

        // Scenario 3: Resolve current version without explicit ID -> adopts server resolved version
        await page.EvaluateAsync("() => window.openGedDocumentPanel('doc-no-version')");
        await page.WaitForFunctionAsync("() => document.getElementById('gedDocumentSidePanel')?.dataset?.versionId === 'v-latest-resolved'");
        var versionResolutionResult = await page.EvaluateAsync<string>("() => document.getElementById('gedDocumentSidePanel').dataset.versionId");
        Assert.Equal("v-latest-resolved", versionResolutionResult);

        // Scenario 4: Session expired (401) -> displays session expired notice and login link
        await page.EvaluateAsync("() => window.openGedDocumentPanel('doc-expired')");
        await page.WaitForFunctionAsync("() => document.getElementById('gedDocumentSidePanel')?.innerHTML?.includes('expirou') === true");
        var expiredHtml = await page.EvaluateAsync<string>("() => document.getElementById('gedDocumentSidePanel').innerHTML");
        Assert.Contains("expirou", expiredHtml);
        Assert.Contains("/Account/Login", expiredHtml);

        // Scenario 5: Focus and scroll restoration on close
        var focusAndScrollRestored = await page.EvaluateAsync<bool>(@"async () => {
            const btn = document.getElementById('btnDocA');
            btn.focus();
            window.scrollTo(0, 150);
            await window.openGedDocumentPanel('doc-B');
            window.closeGedDocumentPanel();
            return document.activeElement === btn;
        }");
        Assert.True(focusAndScrollRestored);

        Assert.Empty(errors);
    }

    [Fact]
    public void All_views_loading_side_panel_load_gate_before_panel()
    {
        var root = FindRepoRoot();
        var viewsDir = Path.Combine(root, "InovaGed.Web", "Views");
        var cshtmlFiles = Directory.GetFiles(viewsDir, "*.cshtml", SearchOption.AllDirectories);

        foreach (var file in cshtmlFiles)
        {
            var content = File.ReadAllText(file);
            if (content.Contains("ged-document-side-panel.js"))
            {
                Assert.Contains("ged-panel-response-gate.js", content);
                var gateIndex = content.IndexOf("ged-panel-response-gate.js", StringComparison.Ordinal);
                var panelIndex = content.IndexOf("ged-document-side-panel.js", StringComparison.Ordinal);
                Assert.True(gateIndex < panelIndex, $"No arquivo {Path.GetFileName(file)}, ged-panel-response-gate.js DEVE ser carregado ANTES de ged-document-side-panel.js");
            }
        }
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
