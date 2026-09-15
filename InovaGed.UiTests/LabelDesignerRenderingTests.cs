using Microsoft.Playwright;
using Xunit;

namespace InovaGed.UiTests;

public sealed class LabelDesignerRenderingTests
{
    [Theory]
    [InlineData(360, 800)]
    [InlineData(390, 844)]
    [InlineData(768, 1024)]
    [InlineData(1024, 768)]
    [InlineData(1366, 768)]
    [InlineData(1920, 1080)]
    [Trait("Category", "UI")]
    public async Task Authenticated_editor_completes_rendering_and_initializes_without_browser_errors(int width, int height)
    {
        var baseUrl=Environment.GetEnvironmentVariable("INOVAGED_UI_BASE_URL");
        var templateKey=Environment.GetEnvironmentVariable("INOVAGED_UI_LABEL_TEMPLATE_KEY");
        if(string.IsNullOrWhiteSpace(baseUrl)||string.IsNullOrWhiteSpace(templateKey))
        {
            if(string.Equals(Environment.GetEnvironmentVariable("CI"),"true",StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("INOVAGED_UI_BASE_URL e INOVAGED_UI_LABEL_TEMPLATE_KEY são obrigatórios no CI.");
            return;
        }

        using var playwright=await Playwright.CreateAsync();
        await using var browser=await playwright.Chromium.LaunchAsync(new(){Headless=true});
        var context=await browser.NewContextAsync(new(){BaseURL=baseUrl,ViewportSize=new(){Width=width,Height=height},Locale="pt-BR",ReducedMotion=ReducedMotion.Reduce});
        var page=await context.NewPageAsync();
        var browserErrors=new List<string>();
        var requiredAssets=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        page.Console+=(_,message)=>{if(message.Type=="error")browserErrors.Add(message.Text);};
        page.PageError+=(_,error)=>browserErrors.Add(error);
        page.Response+=(_,response)=>
        {
            if(response.Url.Contains("labels-designer.css",StringComparison.OrdinalIgnoreCase)||response.Url.Contains("labels-designer.js",StringComparison.OrdinalIgnoreCase))
            { requiredAssets.Add(new Uri(response.Url).AbsolutePath); Assert.True(response.Ok,$"Recurso {response.Url} retornou {response.Status}."); }
        };

        await LoginAsync(page,baseUrl);
        var response=await page.GotoAsync($"/Labels/Designer/Edit/{Uri.EscapeDataString(templateKey)}",new(){WaitUntil=WaitUntilState.NetworkIdle});
        Assert.NotNull(response);Assert.True(response!.Ok,$"Editor retornou HTTP {response.Status}.");
        Assert.DoesNotContain("Ocorreu um erro",await page.Locator("body").InnerTextAsync(),StringComparison.OrdinalIgnoreCase);
        await Assertions.Expect(page.Locator("[data-designer] .canvas-toolbar")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-designer] [data-canvas]")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-action='save']")).ToHaveCountAsync(1);
        Assert.Contains(requiredAssets,x=>x.Contains("labels-designer.css",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(requiredAssets,x=>x.Contains("labels-designer.js",StringComparison.OrdinalIgnoreCase));
        Assert.Empty(browserErrors);
    }

    private static async Task LoginAsync(IPage page,string baseUrl)
    {
        var password=Environment.GetEnvironmentVariable("INOVAGED_UI_PASSWORD")??throw new InvalidOperationException("INOVAGED_UI_PASSWORD não configurada.");
        await page.GotoAsync(new Uri(new Uri(baseUrl),"/Account/Login").ToString());
        await page.GetByLabel("E-mail ou CPF").FillAsync("admin@inovaged.local");
        await page.GetByLabel("Senha",new(){Exact=true}).FillAsync(password);
        await page.GetByRole(AriaRole.Button,new(){Name="Entrar"}).ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }
}
