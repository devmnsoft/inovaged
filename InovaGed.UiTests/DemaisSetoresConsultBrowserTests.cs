using Microsoft.Playwright;
using Xunit;

namespace InovaGed.UiTests;

public sealed class DemaisSetoresConsultBrowserTests
{
    [Fact]
    public async Task Chromium_requires_explicit_encounter_and_discards_stale_lookup_and_search()
    {
        var root = FindRepoRoot();
        var script = Path.Combine(root, "InovaGed.Web", "wwwroot", "js", "demais-setores.js");
        var css = Path.Combine(root, "InovaGed.Web", "wwwroot", "css", "demais-setores.css");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync(new BrowserNewPageOptions { ViewportSize = new ViewportSize { Width = 1366, Height = 768 } });
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        var lookups = new List<string>();
        var searches = new List<string>();
        await page.RouteAsync("**/buscar", async route =>
        {
            var body = route.Request.PostData ?? "";
            lookups.Add(body);
            if (body.Contains("lento"))
            {
                await Task.Delay(700);
                await route.FulfillAsync(Json(Found("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "ATD-LENTO")));
                return;
            }
            if (body.Contains("rapido"))
            {
                await route.FulfillAsync(new RouteFulfillOptions { Status = 200, ContentType = "application/json", Body = """{"status":"NotFound","message":"Nenhum paciente foi localizado com esse código na origem informada.","patient":null,"encounters":[]}""" });
                return;
            }
            if (body.Contains("vazio"))
            {
                await route.FulfillAsync(Json("""{"status":"Found","message":"Paciente localizado na origem.","patient":{"id":"11111111-1111-1111-1111-111111111111","external_code":"000120","display_name":"Paciente sem episódio"},"encounters":[]}"""));
                return;
            }
            if (body.Contains("falha"))
            {
                await route.FulfillAsync(new RouteFulfillOptions { Status = 500, ContentType = "text/html", Body = "<html>erro</html>" });
                return;
            }
            await route.FulfillAsync(Json(Found("22222222-2222-2222-2222-222222222222", "ATD-1", "33333333-3333-3333-3333-333333333333", "ATD-2")));
        });
        await page.RouteAsync("**/pesquisar", async route =>
        {
            var body = route.Request.PostData ?? "";
            searches.Add(body);
            if (body.Contains("html"))
            {
                await route.FulfillAsync(new RouteFulfillOptions { Status = 500, ContentType = "text/html", Body = "<html>falha</html>" });
                return;
            }
            var pageNumber = FormPage(body);
            var many = body.Contains("paginas");
            await route.FulfillAsync(Json(Search(pageNumber, many, many ? 2000 : 2)));
        });
        await page.SetContentAsync(ConsultHtml(embedded: false));
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Path = css });
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Path = script });

        await page.FillAsync("[name='externalCode']", "ok");
        await page.ClickAsync("[data-ds-find]");
        await page.WaitForFunctionAsync("() => document.querySelector('[data-ds-context]').textContent.includes('habilitar a abertura')");
        Assert.Equal("", await page.InputValueAsync("[data-ds-encounters]"));
        Assert.Equal("true", await page.GetAttributeAsync("[data-ds-context-open]", "aria-disabled"));
        Assert.Null(await page.GetAttributeAsync("[data-ds-context-open]", "href"));
        Assert.Contains("situação não informada", await page.Locator("[data-ds-encounters] option").Nth(1).InnerTextAsync());

        await page.SelectOptionAsync("[data-ds-encounters]", "33333333-3333-3333-3333-333333333333");
        Assert.Equal("false", await page.GetAttributeAsync("[data-ds-context-open]", "aria-disabled"));
        Assert.Contains("33333333-3333-3333-3333-333333333333", await page.GetAttributeAsync("[data-ds-context-open]", "href"));

        await page.FillAsync("[name='externalCode']", "lento");
        await page.ClickAsync("[data-ds-find]");
        await page.FillAsync("[name='externalCode']", "rapido");
        await page.ClickAsync("[data-ds-find]");
        await page.WaitForFunctionAsync("() => document.querySelector('[data-ds-context]').textContent.includes('Nenhum paciente foi localizado')");
        await Task.Delay(900);
        Assert.Contains("Nenhum paciente foi localizado", await page.InnerTextAsync("[data-ds-context]"));
        Assert.Equal("true", await page.GetAttributeAsync("[data-ds-context-open]", "aria-disabled"));
        Assert.Null(await page.GetAttributeAsync("[data-ds-context-open]", "href"));

        await page.FillAsync("[name='externalCode']", "vazio");
        await page.ClickAsync("[data-ds-find]");
        await page.WaitForFunctionAsync("() => document.querySelector('[data-ds-context]').textContent.includes('Não há atendimentos')");
        Assert.Equal("true", await page.GetAttributeAsync("[data-ds-context-open]", "aria-disabled"));

        await page.FillAsync("[name='externalCode']", "falha");
        await page.ClickAsync("[data-ds-find]");
        await page.WaitForFunctionAsync("() => document.querySelector('[data-ds-context]').textContent.includes('não pôde ser lida')");
        Assert.Equal("true", await page.GetAttributeAsync("[data-ds-context-open]", "aria-disabled"));

        await page.ClickAsync("[data-ds-search] button[type='submit']");
        await page.WaitForSelectorAsync("[data-ds-range]");
        Assert.Contains("Mostrando 1–2 de 2", await page.InnerTextAsync("[data-ds-range]"));
        Assert.True(await page.IsDisabledAsync("[data-ds-prev]"));
        await page.FillAsync("[name='term']", "html");
        await page.ClickAsync("[data-ds-search] button[type='submit']");
        await page.WaitForFunctionAsync("() => document.querySelector('[data-ds-hits]').textContent.includes('formato utilizável')");
        Assert.Equal("html", await page.InputValueAsync("[name='term']"));

        await page.FillAsync("[name='term']", "paginas");
        await page.ClickAsync("[data-ds-search] button[type='submit']");
        await page.WaitForFunctionAsync("() => document.querySelector('[data-ds-range]').textContent.includes('além')");
        Assert.False(await page.IsDisabledAsync("[data-ds-next]"));
        await page.EvaluateAsync("() => { document.querySelector('[data-ds-page]').value = '49'; }");
        await page.ClickAsync("[data-ds-next]");
        await page.WaitForFunctionAsync("() => document.querySelector('[data-ds-next]').disabled");
        Assert.Equal(50, FormPage(searches[^1]));

        var shot = Path.Combine(Path.GetTempPath(), "ds-consult-1366.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = shot, FullPage = true });
        foreach (var width in new[] { 360, 768, 1920 })
        {
            await page.SetViewportSizeAsync(width, 900);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(Path.GetTempPath(), $"ds-consult-{width}.png"), FullPage = true });
        }
        await page.SetViewportSizeAsync(1366, 768);
        await page.EvaluateAsync("() => { document.body.style.zoom = '2'; }");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(Path.GetTempPath(), "ds-consult-1366-zoom2.png"), FullPage = true });
        Assert.True(File.Exists(shot));
        Assert.Empty(errors);
    }

    [Fact]
    public async Task Chromium_accepts_parent_postmessage_and_rejects_another_frame()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var context = await browser.NewContextAsync();
        var switches = new List<string>();
        var root = FindRepoRoot();
        var script = await File.ReadAllTextAsync(Path.Combine(root, "InovaGed.Web", "wwwroot", "js", "demais-setores.js"));
        await context.RouteAsync("https://parceiro.exemplo/**", async route =>
        {
            var url = route.Request.Url;
            if (url.Contains("/trocar"))
            {
                switches.Add(route.Request.PostData ?? "");
                await route.FulfillAsync(new RouteFulfillOptions { Status = 200, ContentType = "text/html", Body = "ok" });
                return;
            }
            if (url.EndsWith("/evil", StringComparison.Ordinal))
            {
                await route.FulfillAsync(new RouteFulfillOptions { ContentType = "text/html", Body = "<!doctype html><title>evil</title><p>outro quadro</p>" });
                return;
            }
            if (url.EndsWith("/consult", StringComparison.Ordinal))
            {
                await route.FulfillAsync(new RouteFulfillOptions { ContentType = "text/html", Body = ConsultHtml(embedded: true) + "<script>" + script + "</script>" });
                return;
            }
            await route.FulfillAsync(new RouteFulfillOptions
            {
                ContentType = "text/html",
                Body = """
<!doctype html><html><body>
<iframe id="consult" src="https://parceiro.exemplo/consult"></iframe>
<iframe id="evil" src="https://parceiro.exemplo/evil"></iframe>
</body></html>
"""
            });
        });
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync("https://parceiro.exemplo/parent");
        await page.FrameLocator("#consult").Locator("[data-ds-workspace]").WaitForAsync();
        await page.EvaluateAsync("() => document.getElementById('evil').contentWindow.eval(\"parent.document.getElementById('consult').contentWindow.postMessage({tipo:'ds-atendimento',codigoAtendimento:'ATD-EVIL'}, 'https://parceiro.exemplo')\")");
        await Task.Delay(300);
        Assert.Empty(switches);
        await page.EvaluateAsync("() => document.getElementById('consult').contentWindow.postMessage({tipo:'ds-altura', altura:10}, 'https://parceiro.exemplo')");
        await page.EvaluateAsync("() => document.getElementById('consult').contentWindow.postMessage({tipo:'ds-atendimento', codigoAtendimento:'ATD-2'}, 'https://parceiro.exemplo')");
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (switches.Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        Assert.Contains(switches, item => item.Contains("ATD-2"));
        Assert.DoesNotContain(switches, item => item.Contains("ATD-EVIL"));
        Assert.Empty(errors);
    }

    private static int FormPage(string body)
    {
        var encoded = System.Text.RegularExpressions.Regex.Match(body, @"(?:^|&)page=(\d+)");
        if (encoded.Success) return int.Parse(encoded.Groups[1].Value);
        var multi = System.Text.RegularExpressions.Regex.Match(body, "name=\"page\"\\s*\\r?\\n\\r?\\n(\\d+)");
        return multi.Success ? int.Parse(multi.Groups[1].Value) : 0;
    }

    private static RouteFulfillOptions Json(string body) => new() { Status = 200, ContentType = "application/json", Body = body };

    private static string Found(string firstId, string firstCode, string? secondId = null, string? secondCode = null)
    {
        var second = secondId is null ? "" : $",{{\"id\":\"{secondId}\",\"external_code\":\"{secondCode}\",\"raw_kind\":\"A\",\"kind\":\"AMBULATORIAL\",\"admitted_at\":\"2026-10-02T11:30:00Z\",\"discharged_at\":\"2026-10-03T08:00:00Z\",\"status\":\"alta\"}}";
        return $$"""
{"status":"Found","message":"Paciente localizado na origem.","patient":{"id":"11111111-1111-1111-1111-111111111111","external_code":"000120","display_name":"Paciente de teste"},"encounters":[{"id":"{{firstId}}","external_code":"{{firstCode}}","raw_kind":"I","kind":null,"admitted_at":"2026-10-01T10:00:00Z","discharged_at":null,"status":null}{{second}}]}
""";
    }

    private static string Search(int page, bool truncated, int total)
    {
        var hits = total > 2
            ? """[{"name":"Prescrição","encounterCode":"ATD-1","encounterId":"22222222-2222-2222-2222-222222222222","versionId":"44444444-4444-4444-4444-444444444444","page":null,"excerpt":"dipirona","textOrigin":"texto da versão"}]"""
            : """[{"name":"Prescrição","encounterCode":"ATD-1","encounterId":"22222222-2222-2222-2222-222222222222","versionId":"44444444-4444-4444-4444-444444444444","page":2,"excerpt":"dipirona","textOrigin":"texto da versão"},{"name":"Evolução","encounterCode":"ATD-9","encounterId":"55555555-5555-5555-5555-555555555555","versionId":"66666666-6666-6666-6666-666666666666","page":null,"excerpt":"outro","textOrigin":"texto da versão"}]""";
        return $$"""
{"mode":"literal","message":"Busca literal.","hits":{{hits}},"total":{{total}},"page":{{page}},"pageSize":20,"pageLimit":50,"reachable":1020,"truncated":{{(truncated ? "true" : "false")}}}
""";
    }

    private static string ConsultHtml(bool embedded) => $$"""
<!doctype html><html lang="pt-BR"><body>
<main class="ds-page" data-ds-consult data-embedded="{{(embedded ? "1" : "0")}}" data-public-id="77777777-7777-7777-7777-777777777777" data-partner-origin="https://parceiro.exemplo" data-allow-switch="{{(embedded ? "1" : "0")}}" data-patient-id="11111111-1111-1111-1111-111111111111" data-encounter-id="22222222-2222-2222-2222-222222222222" data-encounter-code="ATD-1" data-find-url="https://inovaged.local/buscar" data-switch-url="https://parceiro.exemplo/trocar" data-search-url="https://inovaged.local/pesquisar" data-text-url="https://inovaged.local/texto" data-file-url="https://inovaged.local/arquivo">
<input name="__RequestVerificationToken" value="token" />
<section data-ds-consult-lookup>
<form data-ds-patient-form><input name="externalCode" /><button type="button" data-ds-find>Consultar paciente</button></form>
<p data-ds-context class="ds-note">Busque pelo ID externo ou CPF e confirme o paciente antes de escolher o atendimento.</p>
<label>Atendimento<select data-ds-encounters><option value="">Selecione um atendimento</option></select></label>
<a data-ds-context-open aria-disabled="true">Abrir o atendimento confirmado</a>
</section>
<button type="button" class="ds-doc" data-ds-open data-url="/arquivo?versionId=44444444-4444-4444-4444-444444444444" data-version="44444444-4444-4444-4444-444444444444" data-name="Prescrição médica com nome institucional muito longo para verificar quebra de linha sem sobrepor o visualizador">Prescrição médica com nome institucional muito longo para verificar quebra de linha sem sobrepor o visualizador</button>
<div data-ds-workspace>
<p data-ds-viewer-status>Nenhum documento selecionado.</p>
<iframe data-ds-frame title="Documento"></iframe>
<p data-ds-text></p>
<form data-ds-search>
<label>Palavra<input name="term" value="dipirona" /></label>
<input type="hidden" name="page" value="0" data-ds-page />
<button type="submit">Buscar texto</button>
</form>
<ul data-ds-hits></ul>
<nav data-ds-pager hidden>
<button type="button" data-ds-prev>Anterior</button>
<p data-ds-range></p>
<button type="button" data-ds-next>Próxima</button>
</nav>
</div>
</main>
</body></html>
""";

    [Fact]
    public async Task Chromium_partner_page_builds_a_template_and_keeps_two_frames()
    {
        var root = FindRepoRoot();
        var html = await File.ReadAllTextAsync(Path.Combine(root, "tools", "parceiro-demais-setores", "index.html"));
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.SetContentAsync(html);
        await page.FillAsync("#paciente", "000120");
        await page.FillAsync("#documento", "Prescrição Médica");
        await page.ClickAsync("#montar");
        var model = await page.InputValueAsync("#modelo");
        Assert.Contains("tipoIdentificador=ID", model, StringComparison.Ordinal);
        Assert.Contains("paciente=000120", model, StringComparison.Ordinal);
        Assert.Contains("codigoAtendimento=000088", model, StringComparison.Ordinal);
        Assert.Contains("Prescri", model, StringComparison.Ordinal);
        Assert.DoesNotContain("chave", model, StringComparison.OrdinalIgnoreCase);
        await page.FillAsync("#pagina", "/DemaisSetores/Abrir/pagina");
        await page.FillAsync("#quadro", "/DemaisSetores/Abrir/quadro");
        await page.ClickAsync("#incorporar");
        Assert.Contains("/DemaisSetores/Abrir/pagina", await page.GetAttributeAsync("#frame-a", "src"), StringComparison.Ordinal);
        Assert.Contains("/DemaisSetores/Abrir/quadro", await page.GetAttributeAsync("#frame-b", "src"), StringComparison.Ordinal);
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
