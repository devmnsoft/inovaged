using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace InovaGed.UiTests;

public sealed class DocumentMoveBrowserTests
{
    [Fact]
    public async Task Single_move_searches_the_server_and_refreshes_the_origin()
    {
        await using var f=await BrowserFixture.CreateAsync();
        await f.Page.RouteAsync("**/Ged/Documents/Move",async route=>
        {
            using var request=JsonDocument.Parse(route.Request.PostData!);
            Assert.Equal("doc-a",request.RootElement.GetProperty("documentId").GetString());
            Assert.Equal("real-target",request.RootElement.GetProperty("destinationFolderId").GetString());
            await route.FulfillAsync(new(){ContentType="application/json",Body="""{"success":true,"message":"Documento movido","data":{"documentId":"doc-a","success":true,"historyId":"history-a"}}"""});
        });
        await f.Page.Locator("#doc-a .js-move-one").ClickAsync();
        await f.Page.Locator(".js-folder-result").WaitForAsync();
        await f.Page.Locator(".js-folder-result").ClickAsync();
        await f.Page.Locator("#btnConfirmMove").ClickAsync();
        await f.Page.WaitForFunctionAsync("() => !document.getElementById('doc-a')");
        await Assertions.Expect(f.Page.Locator("#moveDocumentsModal")).ToBeHiddenAsync();
        await Assertions.Expect(f.Page.Locator(".modal-backdrop")).ToHaveCountAsync(0);
        await Assertions.Expect(f.Page.Locator(".ig-toast--success")).ToContainTextAsync("Documento movido");
        Assert.Equal(0,await f.Page.Locator("#doc-a").CountAsync());
        Assert.Empty(await f.Page.EvaluateAsync<string[]>("window.GedSelection.ids()"));
        Assert.Empty(f.Errors);
    }

    [Fact]
    public async Task Dragging_a_selection_preserves_failed_items_and_exposes_partial_failure_details()
    {
        await using var f=await BrowserFixture.CreateAsync();
        await f.Page.RouteAsync("**/Ged/Documents/MoveBulk",async route=>
        {
            using var request=JsonDocument.Parse(route.Request.PostData!);
            Assert.Equal(2,request.RootElement.GetProperty("documentIds").GetArrayLength());
            Assert.Equal("virtual-target",request.RootElement.GetProperty("requestedFolderId").GetString());
            Assert.False(request.RootElement.GetProperty("allOrNothing").GetBoolean());
            await route.FulfillAsync(new(){ContentType="application/json",Body="""{"success":false,"message":"1 movido e 1 falha","correlationId":"move-test","data":{"successCount":1,"failCount":1,"items":[{"documentId":"doc-a","success":true,"historyId":"history-a"},{"documentId":"doc-b","success":false,"errorCode":"ACCESS_DENIED","message":"Sem permissão"}]}}"""});
        });
        await f.Page.EvaluateAsync("window.GedSelection.replace(['doc-a','doc-b'])");
        await f.Page.Locator("#doc-a .drag-handle").DragToAsync(f.Page.Locator("#destination"));
        await f.Page.Locator("#btnConfirmMove").ClickAsync();
        await f.Page.WaitForFunctionAsync("() => !document.getElementById('doc-a')");
        Assert.Equal(new[]{"doc-b"},await f.Page.EvaluateAsync<string[]>("window.GedSelection.ids()"));
        Assert.True(await f.Page.Locator("#moveDocumentsModal").IsVisibleAsync());
        Assert.Contains("Sem permissão",await f.Page.Locator("#moveResultDetails").TextContentAsync());
        Assert.Contains("move-test",await f.Page.Locator("#moveDocumentsMessage").TextContentAsync());
        Assert.Empty(f.Errors);
    }

    private static async Task OpenSingleAsync(BrowserFixture f)
    {
        await f.Page.Locator("#doc-a .js-move-one").ClickAsync();
        await f.Page.WaitForFunctionAsync("() => window.moveDialogReady === true");
        await f.Page.Locator(".js-folder-result").ClickAsync();
        await Assertions.Expect(f.Page.Locator("#btnConfirmMove")).ToBeEnabledAsync();
    }

    [Fact]
    public async Task Confirm_stays_clickable_on_a_short_screen_above_the_expanded_reader()
    {
        await using var f = await BrowserFixture.CreateAsync();
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "InovaGed.sln"))) root = root.Parent;
        var css = Path.Combine(root!.FullName, "InovaGed.Web", "wwwroot", "css");
        await f.Page.SetViewportSizeAsync(1100, 480);
        await f.Page.AddStyleTagAsync(new() { Path = Path.Combine(css, "ged-explorer.css") });
        await f.Page.AddStyleTagAsync(new() { Path = Path.Combine(css, "pages", "ged-workspace.css") });
        await f.Page.EvaluateAsync("""
() => {
  const panel = document.createElement('aside');
  panel.id = 'expandedReaderCover';
  panel.className = 'ged-document-side-panel is-expanded';
  panel.textContent = 'leitor expandido';
  document.body.appendChild(panel);
}
""");
        await f.Page.EvaluateAsync("() => document.querySelector('#doc-a .js-move-one').click()");
        await f.Page.WaitForFunctionAsync("() => window.moveDialogReady === true");
        await f.Page.Locator(".js-folder-result").ClickAsync();
        await Assertions.Expect(f.Page.Locator("#btnConfirmMove")).ToBeEnabledAsync();
        await AssertConfirmIsReachable(f, 1100, 480);
        await Assertions.Expect(f.Page.Locator("#moveDocumentsTitle")).ToHaveTextAsync("Mover documento");
        await Assertions.Expect(f.Page.Locator("#moveAllOrNothing")).ToBeHiddenAsync();
        await f.Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(f.Page.Locator("#moveDocumentsModal")).ToBeHiddenAsync();

        await f.Page.EvaluateAsync("window.GedSelection.replace(['doc-a','doc-b'])");
        await f.Page.EvaluateAsync("() => document.querySelector('.js-btn-move-selected').click()");
        await f.Page.WaitForFunctionAsync("() => window.moveDialogReady === true");
        await f.Page.Locator(".js-folder-result").ClickAsync();
        await Assertions.Expect(f.Page.Locator("#btnConfirmMove")).ToBeEnabledAsync();
        await AssertConfirmIsReachable(f, 1100, 480);
        await Assertions.Expect(f.Page.Locator("#moveDocumentsTitle")).ToHaveTextAsync("Mover documentos para outra pasta");
        await Assertions.Expect(f.Page.Locator("#moveAllOrNothing")).ToBeVisibleAsync();
        Assert.Empty(f.Errors);
    }

    private static async Task AssertConfirmIsReachable(BrowserFixture f, int width, int height)
    {
        var box = await f.Page.Locator("#btnConfirmMove").BoundingBoxAsync();
        Assert.NotNull(box);
        Assert.True(box.Y >= 0 && box.Y + box.Height <= height, $"Botão fora da altura: y={box.Y} h={box.Height}");
        Assert.True(box.X >= 0 && box.X + box.Width <= width, $"Botão fora da largura: x={box.X} w={box.Width}");
        Assert.True(await f.Page.EvaluateAsync<bool>("""
() => {
  const button = document.getElementById('btnConfirmMove');
  const rect = button.getBoundingClientRect();
  const target = document.elementFromPoint(rect.left + rect.width / 2, rect.top + Math.min(rect.height / 2, 8));
  return target === button || button.contains(target);
}
"""));
    }

    [Fact]
    public async Task Confirm_is_the_only_dialog_and_keyboard_can_cancel_and_reopen()
    {
        await using var f = await BrowserFixture.CreateAsync();
        await OpenSingleAsync(f);
        await Assertions.Expect(f.Page.Locator(".modal.show")).ToHaveCountAsync(1);
        await Assertions.Expect(f.Page.Locator("#appConfirmModal")).ToBeHiddenAsync();
        await f.Page.Locator("#btnConfirmMove").FocusAsync();
        await f.Page.Keyboard.PressAsync("Tab");
        Assert.True(await f.Page.EvaluateAsync<bool>("document.getElementById('moveDocumentsModal').contains(document.activeElement)"));
        await f.Page.Keyboard.PressAsync("Shift+Tab");
        await Assertions.Expect(f.Page.Locator("#btnConfirmMove")).ToBeFocusedAsync();
        await f.Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(f.Page.Locator(".modal-backdrop")).ToHaveCountAsync(0);
        await OpenSingleAsync(f);
        await Assertions.Expect(f.Page.Locator("#btnConfirmMove")).ToBeEnabledAsync();
        Assert.Empty(f.Errors);
    }

    [Theory]
    [InlineData(401, "application/json", "{}", "sessão expirou")]
    [InlineData(403, "application/json", "{}", "permissão")]
    [InlineData(400, "application/json", "{\"success\":false,\"message\":\"Token inválido\"}", "Token inválido")]
    [InlineData(500, "application/json", "{\"success\":false,\"message\":\"Erro interno\"}", "Erro interno")]
    [InlineData(200, "text/html", "<html>Login</html>", "não foi confirmado")]
    [InlineData(200, "application/json", "", "não foi confirmado")]
    [InlineData(200, "application/json", "{\"success\":true}", "não foi confirmado")]
    public async Task Failed_or_invalid_response_never_announces_success(int status, string contentType, string body, string expected)
    {
        await using var f = await BrowserFixture.CreateAsync();
        await f.Page.RouteAsync("**/Ged/Documents/Move", r => r.FulfillAsync(new() { Status = status, ContentType = contentType, Body = body }));
        await OpenSingleAsync(f);
        await f.Page.Locator("#btnConfirmMove").ClickAsync();
        await Assertions.Expect(f.Page.Locator("#moveDocumentsMessage")).ToContainTextAsync(expected);
        await Assertions.Expect(f.Page.Locator("#btnConfirmMove")).ToBeEnabledAsync();
        Assert.Equal(0, await f.Page.Locator(".ig-toast--success").CountAsync());
        Assert.Equal(1, await f.Page.Locator("#doc-a").CountAsync());
        await f.Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(f.Page.Locator(".modal-backdrop")).ToHaveCountAsync(0);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public async Task Slow_request_blocks_duplicate_clicks_and_dismissal()
    {
        await using var f = await BrowserFixture.CreateAsync();
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var requests = 0;
        await f.Page.RouteAsync("**/Ged/Documents/Move", async r => {
            Interlocked.Increment(ref requests); started.TrySetResult();
            await release.Task;
            await r.FulfillAsync(new() { ContentType = "application/json", Body = """{"success":true,"message":"Documento movido","data":{"documentId":"doc-a","success":true}}""" });
        });
        await OpenSingleAsync(f);
        await f.Page.EvaluateAsync("() => {const b=document.getElementById('btnConfirmMove');b.click();b.click();b.dispatchEvent(new MouseEvent('click',{bubbles:true}));}");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Assertions.Expect(f.Page.Locator("#btnConfirmMove")).ToBeDisabledAsync();
        await f.Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(f.Page.Locator("#moveDocumentsModal")).ToBeVisibleAsync();
        release.SetResult();
        await Assertions.Expect(f.Page.Locator("#moveDocumentsModal")).ToBeHiddenAsync();
        Assert.Equal(1, requests);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public async Task Refresh_failure_preserves_list_and_retry_never_reposts_move()
    {
        await using var f = await BrowserFixture.CreateAsync();
        var posts = 0;
        await f.Page.RouteAsync("**/Ged/Documents/Move", async r => {
            posts++;
            await r.FulfillAsync(new() { ContentType = "application/json", Body = """{"success":true,"message":"Documento movido","data":{"documentId":"doc-a","success":true}}""" });
        });
        await f.Page.RouteAsync("**/Ged/DocumentsList?*", r => r.FulfillAsync(new() { Status = 500, Body = "Erro" }));
        await OpenSingleAsync(f);
        await f.Page.Locator("#btnConfirmMove").ClickAsync();
        await Assertions.Expect(f.Page.Locator("#moveDocumentsModal")).ToBeHiddenAsync();
        await Assertions.Expect(f.Page.Locator("#moveRefreshWarning")).ToContainTextAsync("Movimentação realizada com sucesso");
        Assert.Equal(1, await f.Page.Locator("#doc-a").CountAsync());
        Assert.Equal(0, await f.Page.Locator(".ig-toast--error").CountAsync());
        await f.Page.UnrouteAsync("**/Ged/DocumentsList?*");
        await f.InstallListingAsync();
        await f.Page.Locator("#moveRefreshWarning button").ClickAsync();
        await Assertions.Expect(f.Page.Locator("#doc-a")).ToHaveCountAsync(0);
        await Assertions.Expect(f.Page.Locator("#moveRefreshWarning")).ToHaveCountAsync(0);
        Assert.Equal(1, posts);
        Assert.Empty(f.Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bulk_success_or_rollback_respects_selection_and_feedback(bool rollback)
    {
        await using var f = await BrowserFixture.CreateAsync();
        await f.Page.RouteAsync("**/Ged/Documents/MoveBulk", async r => {
            using var request = JsonDocument.Parse(r.Request.PostData!);
            Assert.True(request.RootElement.GetProperty("allOrNothing").GetBoolean());
            await r.FulfillAsync(new() { ContentType = "application/json", Body = rollback
                ? """{"success":false,"message":"Lote revertido","data":{"successCount":0,"failCount":2,"rolledBack":true}}"""
                : """{"success":true,"message":"2 documentos movidos","data":{"successCount":2,"failCount":0,"items":[{"documentId":"doc-a","success":true},{"documentId":"doc-b","success":true}]}}""" });
        });
        await f.Page.EvaluateAsync("window.GedSelection.replace(['doc-a','doc-b'])");
        await f.Page.Locator(".js-btn-move-selected").ClickAsync();
        await f.Page.Locator(".js-folder-result").ClickAsync();
        await f.Page.Locator("#moveAllOrNothing").CheckAsync();
        await f.Page.Locator("#btnConfirmMove").ClickAsync();
        if (rollback) {
            await Assertions.Expect(f.Page.Locator("#moveDocumentsMessage")).ToContainTextAsync("Lote revertido");
            Assert.Equal(2, (await f.Page.EvaluateAsync<string[]>("window.GedSelection.ids()")).Length);
            Assert.Equal(0, await f.Page.Locator(".ig-toast--success").CountAsync());
        } else {
            await Assertions.Expect(f.Page.Locator("#moveDocumentsModal")).ToBeHiddenAsync();
            Assert.Empty(await f.Page.EvaluateAsync<string[]>("window.GedSelection.ids()"));
            await Assertions.Expect(f.Page.Locator(".ig-toast--success")).ToContainTextAsync("2 documentos movidos");
        }
        Assert.Empty(f.Errors);
    }

    [Fact]
    public async Task Timeout_keeps_idempotency_for_retry_and_releases_controls()
    {
        await using var f = await BrowserFixture.CreateAsync();
        await f.Page.Clock.InstallAsync();
        var keys = new List<string>();
        var started = new TaskCompletionSource();
        await f.Page.RouteAsync("**/Ged/Documents/Move", async r => {
            using var request = JsonDocument.Parse(r.Request.PostData!);
            keys.Add(request.RootElement.GetProperty("idempotencyKey").GetString()!);
            if (keys.Count == 1) { started.SetResult(); return; }
            await r.FulfillAsync(new() { ContentType = "application/json", Body = """{"success":true,"message":"Movimentação confirmada","data":{"documentId":"doc-a","success":true}}""" });
        });
        await OpenSingleAsync(f);
        await f.Page.Locator("#btnConfirmMove").ClickAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await f.Page.Clock.FastForwardAsync(31000);
        await Assertions.Expect(f.Page.Locator("#moveDocumentsMessage")).ToContainTextAsync("Tempo limite excedido");
        await Assertions.Expect(f.Page.Locator("#btnConfirmMove")).ToBeEnabledAsync();
        await f.Page.Locator("#btnConfirmMove").ClickAsync();
        await Assertions.Expect(f.Page.Locator("#moveDocumentsModal")).ToBeHiddenAsync();
        Assert.Equal(2, keys.Count);
        Assert.Equal(keys[0], keys[1]);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public async Task Network_failure_recovers_and_changed_reason_gets_a_new_operation_key()
    {
        await using var f = await BrowserFixture.CreateAsync();
        var keys = new List<string>();
        await f.Page.RouteAsync("**/Ged/Documents/Move", async r => {
            using var request = JsonDocument.Parse(r.Request.PostData!);
            keys.Add(request.RootElement.GetProperty("idempotencyKey").GetString()!);
            await r.AbortAsync();
        });
        await OpenSingleAsync(f);
        await f.Page.Locator("#btnConfirmMove").ClickAsync();
        await Assertions.Expect(f.Page.Locator("#moveDocumentsMessage")).ToContainTextAsync("Falha de comunicação");
        await f.Page.Locator("#moveReason").FillAsync("Outro motivo");
        await f.Page.Locator("#btnConfirmMove").ClickAsync();
        await Assertions.Expect(f.Page.Locator("#btnConfirmMove")).ToBeEnabledAsync();
        Assert.Equal(2, keys.Count);
        Assert.NotEqual(keys[0], keys[1]);
        Assert.Equal(0, await f.Page.Locator(".ig-toast--success").CountAsync());
        Assert.Empty(f.Errors);
    }

    [Fact]
    public async Task Navigation_loads_real_destination_and_ignores_older_responses()
    {
        await using var f = await BrowserFixture.CreateAsync();
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        await f.Page.RouteAsync("**/Ged/DocumentsList?*", async r => {
            var destination = r.Request.Url.Contains("folderId=real-target");
            if (!destination) { started.SetResult(); await release.Task; }
            await r.FulfillAsync(new() { ContentType = "text/html", Body = destination
                ? """<div id="gedDocumentsContainer" data-visual-folder-id="virtual-target" data-listing-folder-id="real-target" data-folder-name="Destino" data-document-count="1"><div id="doc-a" data-document-id="doc-a">Documento A</div></div>"""
                : """<div id="gedDocumentsContainer" data-visual-folder-id="origin" data-listing-folder-id="origin-real">Resposta antiga</div>""" });
        });
        await f.Page.EvaluateAsync("() => {window.oldRefresh=window.GedFolderNavigation.refreshCurrentFolder();}");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await f.Page.Locator("#destination").ClickAsync();
        await Assertions.Expect(f.Page.Locator("#gedDocumentsContainer")).ToHaveAttributeAsync("data-listing-folder-id", "real-target");
        release.SetResult();
        Assert.True(await f.Page.EvaluateAsync<bool>("async () => (await window.oldRefresh).superseded === true"));
        await Assertions.Expect(f.Page.Locator("#gedDocumentsContainer")).ToHaveAttributeAsync("data-listing-folder-id", "real-target");
        await Assertions.Expect(f.Page.Locator("#doc-a")).ToContainTextAsync("Documento A");
        Assert.Empty(f.Errors);
    }

    [Fact]
    public async Task Undo_retains_the_single_document_history_after_selection_is_cleared()
    {
        await using var f = await BrowserFixture.CreateAsync();
        await f.Page.RouteAsync("**/Ged/Documents/Move", r => r.FulfillAsync(new() { ContentType = "application/json", Body = """{"success":true,"message":"Documento movido","data":{"success":true,"historyId":"history-a"}}""" }));
        var undone = new TaskCompletionSource();
        await f.Page.RouteAsync("**/Ged/Documents/UndoMove", async r => {
            using var request = JsonDocument.Parse(r.Request.PostData!);
            Assert.Equal("doc-a", request.RootElement.GetProperty("documentId").GetString());
            Assert.Equal("history-a", request.RootElement.GetProperty("historyId").GetString());
            await r.FulfillAsync(new() { ContentType = "application/json", Body = """{"success":true,"message":"Movimentação desfeita"}""" });
            undone.SetResult();
        });
        await OpenSingleAsync(f);
        await f.Page.Locator("#btnConfirmMove").ClickAsync();
        await Assertions.Expect(f.Page.Locator("#moveDocumentsModal")).ToBeHiddenAsync();
        await f.Page.Locator("[data-workspace-undo] .btn-link").ClickAsync();
        await undone.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Assertions.Expect(f.Page.Locator(".ig-toast--success").Last).ToContainTextAsync("Movimentação desfeita");
        Assert.Empty(f.Errors);
    }

    private sealed class BrowserFixture : IAsyncDisposable
    {
        private readonly IPlaywright _playwright;
        private readonly IBrowser _browser;
        public IPage Page {get;}
        public List<string> Errors {get;}=[];
        private BrowserFixture(IPlaywright playwright,IBrowser browser,IPage page){_playwright=playwright;_browser=browser;Page=page;page.PageError+=(_,error)=>Errors.Add(error);}
        public Task InstallListingAsync() => Page.RouteAsync("**/Ged/DocumentsList?*", r => r.FulfillAsync(new() { ContentType = "text/html", Body = """<div id="gedDocumentsContainer" data-visual-folder-id="origin" data-listing-folder-id="origin-real" data-folder-name="Origem" data-document-count="1"><div id="doc-b" data-document-id="doc-b"><input class="js-doc-select" value="doc-b" type="checkbox"/></div></div>""" }));
        public static async Task<BrowserFixture> CreateAsync()
        {
            var root=new DirectoryInfo(AppContext.BaseDirectory);
            while(root is not null&&!File.Exists(Path.Combine(root.FullName,"InovaGed.sln")))root=root.Parent;
            var web=Path.Combine(root!.FullName,"InovaGed.Web");
            var index=await File.ReadAllTextAsync(Path.Combine(web,"Views","Ged","Index.cshtml"));
            var start=index.IndexOf("<div class=\"modal fade\" id=\"moveDocumentsModal\"",StringComparison.Ordinal);
            var end=index.IndexOf("<div class=\"modal fade\" id=\"modalAddDocumentPart\"",start,StringComparison.Ordinal);
            var html="""
<!DOCTYPE html><html><head><style>.drag-handle{display:block;padding:20px}#destination{display:block;padding:30px}</style></head><body>
<input name="__RequestVerificationToken" value="token" type="hidden"/>
<div id="gedDocumentsContainer" data-folder-name="Origem" data-visual-folder-id="origin" data-listing-folder-id="origin-real">
<div id="doc-a" data-document-id="doc-a" draggable="true"><span class="drag-handle">Documento A</span><input class="js-doc-select" value="doc-a" type="checkbox"/><button class="js-move-one" data-document-id="doc-a">Mover A</button></div>
<div id="doc-b" data-document-id="doc-b" draggable="true"><span class="drag-handle">Documento B</span><input class="js-doc-select" value="doc-b" type="checkbox"/></div></div>
<button class="js-btn-move-selected">Mover selecionados</button>
<div id="destination" class="js-folder-node" data-folder-id="virtual-target" data-upload-folder-id="real-target" data-folder-name="Destino" data-can-receive-documents="true">Destino</div>
"""+index[start..end]+await File.ReadAllTextAsync(Path.Combine(web,"Views","Shared","Feedback","_ConfirmDialog.cshtml"))+"<div id=\"appToastContainer\" class=\"ig-toast-region\"></div></body></html>";
            var playwright=await Playwright.CreateAsync();
            var browser=await playwright.Chromium.LaunchAsync(new(){Headless=true});
            var page=await browser.NewPageAsync();
            var f=new BrowserFixture(playwright,browser,page);
            await f.InstallListingAsync();
            await page.RouteAsync("http://inovaged.test/Ged",r=>r.FulfillAsync(new(){ContentType="text/html",Body=html}));
            await page.RouteAsync("**/Ged/Folders/Search*",r=>r.FulfillAsync(new(){ContentType="application/json",Body="""{"success":true,"items":[{"id":"real-target","name":"Destino","canWrite":true}]}"""}));
            await page.GotoAsync("http://inovaged.test/Ged");
            await page.AddStyleTagAsync(new(){Path=Path.Combine(web,"wwwroot","lib","bootstrap","dist","css","bootstrap.min.css")});
            await page.AddStyleTagAsync(new(){Path=Path.Combine(web,"wwwroot","css","atlas","atlas-feedback.css")});
            await page.AddScriptTagAsync(new(){Path=Path.Combine(web,"wwwroot","lib","bootstrap","dist","js","bootstrap.bundle.min.js")});
            foreach(var file in new[]{"inovaged-feedback.js","site.js","workspace-undo.js","ged-document-selection.js","ged-document-move.js","ged-folder-navigation.js"})await page.AddScriptTagAsync(new(){Path=Path.Combine(web,"wwwroot","js",file)});
            await page.EvaluateAsync("""
() => {
    window.moveDialogReady=false;
    const modal=document.getElementById('moveDocumentsModal');
    modal.addEventListener('show.bs.modal',()=>window.moveDialogReady=false);
    modal.addEventListener('shown.bs.modal',()=>window.moveDialogReady=true);
    document.dispatchEvent(new Event('DOMContentLoaded'));
}
""");
            return f;
        }
        public async ValueTask DisposeAsync(){await _browser.DisposeAsync();_playwright.Dispose();}
    }
}
