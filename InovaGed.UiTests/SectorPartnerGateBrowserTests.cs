using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace InovaGed.UiTests;

public sealed class SectorPartnerGateFactAttribute : FactAttribute
{
    public SectorPartnerGateFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("INOVAGED_SECTOR_GATE")!="1")
            Skip="Requires the disposable sector partner fixture and both local applications. See integration documentation.";
    }
}

[Collection("Sector external fixture")]
public sealed class SectorPartnerGateBrowserTests
{
    [SectorPartnerGateFact]
    public async Task Real_login_partner_iframes_lost_response_recovery_and_revocation()
    {
        var app=Environment.GetEnvironmentVariable("INOVAGED_SECTOR_APP") ?? "https://localhost:7189";
        var partner=Environment.GetEnvironmentVariable("INOVAGED_SECTOR_PARTNER") ?? "http://127.0.0.1:5190";
        Assert.StartsWith("https://localhost:",app);
        Assert.True(partner.StartsWith("http://127.0.0.1:") || partner.StartsWith("http://localhost:"));
        var root=FindRoot();
        var evidence=Path.Combine(root,"artifacts/sector-validation/recovery-ui");
        Directory.CreateDirectory(evidence);
        var stages=new List<string>();
        using var summary=new GateEvidence(evidence,stages);
        using var playwright=await Playwright.CreateAsync();
        await using var browser=await playwright.Chromium.LaunchAsync(new() { Headless=true,Channel="chromium" });
        await using var user=await browser.NewContextAsync(new() { IgnoreHTTPSErrors=true,ViewportSize=new() { Width=1366,Height=900 } });
        var page=await user.NewPageAsync();
        var cspBlocked=false;
        page.Console+=(_,message)=> { if(message.Type=="error" && message.Text.Contains("frame-ancestors",StringComparison.OrdinalIgnoreCase))cspBlocked=true; };
        page.SetDefaultTimeout(20000);
        var entry=app+"/DemaisSetores/Integrar?integracao=partner-gate&tipoIdentificador=ID&paciente=000120&codigoAtendimento=000088&nomeDocumento="+Uri.EscapeDataString("Prescrição Médica")+"&modo=documento";
        await page.GotoAsync(entry);
        Assert.Contains("/Account/Login",page.Url);
        Assert.DoesNotContain("000120",page.Url);
        await Login(page);
        await page.WaitForURLAsync("**/ConsultaIntegrada/**");
        await page.Locator("[data-ds-consult]").WaitForAsync();
        Assert.Contains("Paciente sintético",await page.Locator(".ds-context").InnerTextAsync());
        var userId=await page.Locator("[data-ds-consult]").GetAttributeAsync("data-public-id");
        var resource=app+"/DemaisSetores/ConsultaIntegrada/"+userId;
        const string version="50000000-0000-0000-0000-000000000007";
        var pdf=await user.APIRequest.GetAsync(resource+"/arquivo?versionId="+version);
        Assert.Equal(200,pdf.Status);
        Assert.Contains("'self'",pdf.Headers["content-security-policy"]);
        Assert.StartsWith("%PDF-",System.Text.Encoding.ASCII.GetString(await pdf.BodyAsync()));
        var text=await user.APIRequest.GetAsync(resource+"/texto?versionId="+version);
        Assert.Contains("Prescrição de teste",System.Net.WebUtility.HtmlDecode(await text.TextAsync()));
        stages.Add("url-login-documento-pdf-texto");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.EvaluateAsync("document.fonts.ready");
        foreach(var width in new[] { 360,768,1366,1920 })
        {
            await page.SetViewportSizeAsync(width,1000);
            await page.EvaluateAsync("window.scrollTo(0,0)");
            await page.ScreenshotAsync(new() { Path=Path.Combine(evidence,$"consulta-{width}.png") });
            await page.EvaluateAsync("document.documentElement.style.zoom='2'");
            await page.ScreenshotAsync(new() { Path=Path.Combine(evidence,$"consulta-{width}-css-zoom200.png") });
            await page.EvaluateAsync("document.documentElement.style.zoom=''");
            if(await page.Locator("[data-ds-tab='pdf']").IsVisibleAsync())
            {
                await page.Locator("[data-ds-tab='pdf']").ClickAsync();
                Assert.True(await page.Locator("#ds-pdf").IsVisibleAsync());
                Assert.False(await page.Locator("#ds-lista").IsVisibleAsync());
                await page.Keyboard.PressAsync("Home");
                Assert.True(await page.Locator("#ds-lista").IsVisibleAsync());
                await page.Keyboard.PressAsync("ArrowRight");
                Assert.True(await page.Locator("#ds-pdf").IsVisibleAsync());
            }
            await page.Locator("[data-ds-workspace]").ScrollIntoViewIfNeededAsync();
            await page.ScreenshotAsync(new() { Path=Path.Combine(evidence,$"visualizador-{width}.png") });
        }
        Assert.False(cspBlocked);

        // A separate browser context has no grant/authentication cookie. Recovery must be usable,
        // while the opaque public reference alone must never expose clinical content.
        await using var withoutCookie=await browser.NewContextAsync(new() { IgnoreHTTPSErrors=true });
        var recovery=await withoutCookie.NewPageAsync();
        var denied=await recovery.GotoAsync(resource);
        Assert.Equal(401,denied!.Status);
        Assert.DoesNotContain("Paciente sintético",await recovery.Locator("body").InnerTextAsync());
        await recovery.ScreenshotAsync(new() { Path=Path.Combine(evidence,"recuperacao.png") });
        var popup=await withoutCookie.RunAndWaitForPageAsync(async()=>await recovery.GetByRole(AriaRole.Link,new() { Name="Abrir em nova janela" }).ClickAsync());
        await popup.WaitForURLAsync("**/Account/Login?**");
        await Login(popup);
        await popup.WaitForURLAsync("**/ConsultaIntegrada/**");
        Assert.Contains("Paciente sintético",await popup.Locator(".ds-context").InnerTextAsync());
        stages.Add("referencia-sem-cookie-negada-recuperacao-nova-janela-autorizada");

        var blockedPartner=await user.NewPageAsync();
        var blockedCdp=await user.NewCDPSessionAsync(blockedPartner);
        await RestrictThirdParty(blockedCdp);
        await blockedPartner.GotoAsync(partner);
        await blockedPartner.FillAsync("#host",app);
        await blockedPartner.EvaluateAsync("url=>document.getElementById('frame-b').src=url",entry+"&destino=iframe&origemIncorporacao="+Uri.EscapeDataString(partner));
        await blockedPartner.FrameLocator("#frame-b").GetByRole(AriaRole.Link,new() { Name="Abrir em nova janela" }).WaitForAsync();
        Assert.DoesNotContain("Paciente sintético",await blockedPartner.FrameLocator("#frame-b").Locator("body").InnerTextAsync());
        var allowedWindow=await user.RunAndWaitForPageAsync(async()=>await blockedPartner.FrameLocator("#frame-b").GetByRole(AriaRole.Link,new() { Name="Abrir em nova janela" }).ClickAsync());
        await allowedWindow.WaitForURLAsync("**/ConsultaIntegrada/**");
        Assert.Contains("Paciente sintético",await allowedWindow.Locator(".ds-context").InnerTextAsync());
        await blockedPartner.ScreenshotAsync(new() { Path=Path.Combine(evidence,"cookies-terceiros-restritos-recuperacao.png"),FullPage=true });
        stages.Add("cookies-terceiros-restritos-iframe-login-recuperacao-top-level");
        var expiringPublic=await allowedWindow.Locator("[data-ds-consult]").GetAttributeAsync("data-public-id");
        Assert.True(Guid.TryParse(expiringPublic,out var expiringId));
        await ExpireFixtureSession(expiringId);
        await allowedWindow.BringToFrontAsync();
        await allowedWindow.EvaluateAsync("window.dispatchEvent(new Event('focus'))");
        await allowedWindow.Locator("[data-ds-session-recover]").WaitForAsync();
        Assert.False(await allowedWindow.Locator("[data-ds-workspace]").IsVisibleAsync());
        var renewed=await user.RunAndWaitForPageAsync(async()=>await allowedWindow.Locator("[data-ds-session-recover]").ClickAsync());
        await renewed.WaitForURLAsync("**/ConsultaIntegrada/**");
        var renewedPublic=await renewed.Locator("[data-ds-consult]").GetAttributeAsync("data-public-id");
        Assert.NotEqual(expiringPublic,renewedPublic);
        stages.Add("sessao-expirada-recuperada-com-nova-concessao");

        await using var partnerContext=await browser.NewContextAsync(new() { IgnoreHTTPSErrors=true });
        var partnerPage=await partnerContext.NewPageAsync();
        await RestrictThirdParty(await partnerContext.NewCDPSessionAsync(partnerPage));
        partnerPage.SetDefaultTimeout(20000);
        await partnerPage.GotoAsync(partner);
        Assert.Equal("partner-gate",await partnerPage.InputValueAsync("#integracao"));
        Assert.Equal(app,await partnerPage.InputValueAsync("#host"));
        await partnerPage.FillAsync("#host",app);
        await partnerPage.FillAsync("#idempotencia","browser-"+Guid.NewGuid().ToString("N"));
        await partnerPage.ClickAsync("#backend");
        await partnerPage.WaitForFunctionAsync("()=>document.getElementById('quadro').value.includes('/Abrir/')");
        var firstFrame=await partnerPage.InputValueAsync("#quadro");
        await partnerPage.ClickAsync("#incorporar");
        await partnerPage.FrameLocator("#frame-a").Locator("[data-ds-consult]").WaitForAsync();
        await partnerPage.FrameLocator("#frame-b").Locator("[data-ds-consult]").WaitForAsync();
        var framePublic=await partnerPage.FrameLocator("#frame-b").Locator("[data-ds-consult]").GetAttributeAsync("data-public-id");
        Assert.NotEqual(await partnerPage.FrameLocator("#frame-a").Locator("[data-ds-consult]").GetAttributeAsync("data-public-id"),framePublic);
        stages.Add("api-backend-dois-iframes-outra-origem");
        var secondOrigin=new UriBuilder(partner) { Host=new Uri(partner).Host=="localhost"?"127.0.0.1":"localhost" }.Uri.GetLeftPart(UriPartial.Authority);
        var secondPartner=await partnerContext.NewPageAsync();
        await secondPartner.GotoAsync(secondOrigin);
        await secondPartner.FillAsync("#host",app);
        await secondPartner.FillAsync("#idempotencia","second-origin-"+Guid.NewGuid().ToString("N"));
        await secondPartner.ClickAsync("#backend");
        await secondPartner.WaitForFunctionAsync("()=>document.getElementById('quadro').value.includes('/Abrir/')");
        await secondPartner.ClickAsync("#incorporar");
        await secondPartner.FrameLocator("#frame-b").Locator("[data-ds-consult]").WaitForAsync();
        var secondPublic=await secondPartner.FrameLocator("#frame-b").Locator("[data-ds-consult]").GetAttributeAsync("data-public-id");
        Assert.NotEqual(framePublic,secondPublic);
        stages.Add("segunda-origem-autorizada");
        await partnerPage.BringToFrontAsync();
        await partnerPage.EvaluateAsync("['pagina','quadro'].forEach(id=>document.getElementById(id).type='password')");
        await partnerPage.ScreenshotAsync(new() { Path=Path.Combine(evidence,"parceiro-dois-iframes.png"),FullPage=true });

        var wrongOrigin=await partnerPage.EvaluateAsync<int>("""
async()=>{const r=await fetch('/abertura',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({idempotencia:'wrong-'+crypto.randomUUID(),paciente:{tipoIdentificador:'ID',identificador:'000120'},codigoAtendimento:'000088',nomeDocumento:'Prescrição Médica',modo:'documento',origemIncorporacao:'https://wrong.example'})});return r.status;}
""");
        Assert.Equal(422,wrongOrigin);
        stages.Add("origem-incorreta-negada-http");
        await partnerPage.BringToFrontAsync();
        await partnerPage.Locator("#estado").PressAsync("Enter");
        await partnerPage.WaitForFunctionAsync("()=>document.getElementById('resultado').textContent.includes('utilizada')");
        // Simulate losing the response in the partner; repeat identifies the same request,
        // then the authenticated recovery creates a bounded replacement, never a replayed code.
        await partnerPage.FillAsync("#quadro","");
        await partnerPage.FillAsync("#pagina","");
        await partnerPage.Locator("#backend").PressAsync("Enter");
        await partnerPage.WaitForFunctionAsync("()=>document.getElementById('resultado').textContent.includes('já registrada')");
        Assert.Equal("",await partnerPage.InputValueAsync("#quadro"));
        await partnerPage.Locator("#recuperar").PressAsync("Enter");
        await partnerPage.WaitForFunctionAsync("()=>document.getElementById('quadro').value.includes('/Abrir/')");
        Assert.NotEqual(firstFrame,await partnerPage.InputValueAsync("#quadro"));
        var old=await partnerContext.APIRequest.GetAsync(app+"/DemaisSetores/ConsultaIntegrada/"+framePublic+"/estado");
        Assert.Equal(401,old.Status);
        await partnerPage.ClickAsync("#incorporar");
        await partnerPage.FrameLocator("#frame-b").Locator("[data-ds-consult]").WaitForAsync();
        var currentPublic=await partnerPage.FrameLocator("#frame-b").Locator("[data-ds-consult]").GetAttributeAsync("data-public-id");
        stages.Add("resposta-perdida-recuperada-anterior-revogada");
        Assert.Equal(200,(await user.APIRequest.GetAsync(resource+"/arquivo?versionId="+version)).Status);

        await page.BringToFrontAsync();
        await page.GotoAsync(app+"/DemaisSetores/Parametrizar");
        await page.SelectOptionAsync("#ds-url-integracao","partner-gate");
        Assert.Contains("Parceiro sintético de homologação",await page.Locator("#ds-url-integracao option:checked").InnerTextAsync());
        Assert.Equal("",await page.InputValueAsync("#ds-url-origem"));
        await page.SelectOptionAsync("#ds-url-destino","iframe");
        await page.SelectOptionAsync("#ds-url-documento",new SelectOptionValue { Label="Prescrição Médica" });
        await page.ClickAsync("#ds-url-montar");
        Assert.Equal("",await page.InputValueAsync("#ds-url-modelo"));
        await page.SelectOptionAsync("#ds-url-origem",partner);
        await page.ClickAsync("#ds-url-montar");
        Assert.Contains("destino=iframe",await page.InputValueAsync("#ds-url-modelo"));
        Assert.Contains("origemIncorporacao=",await page.InputValueAsync("#ds-url-modelo"));
        var correlation=System.Text.RegularExpressions.Regex.Match(await partnerPage.Locator("#resultado").InnerTextAsync(),"[a-f0-9]{32}").Value;
        await page.FillAsync("#ds-diagnostic-correlation",correlation);
        await page.Locator("[data-ds-diagnostic] button").ClickAsync();
        await page.WaitForFunctionAsync("()=>document.querySelector('[data-ds-diagnostic-result]').textContent.includes('Integração partner-gate')");
        stages.Add("assistente-origem-nao-automatica-diagnostico-correlacao");
        var antiforgery=await page.Locator("input[name='__RequestVerificationToken']").First.InputValueAsync();
        var logoutForm=user.APIRequest.CreateFormData();logoutForm.Set("__RequestVerificationToken",antiforgery);
        await user.APIRequest.PostAsync(app+"/Account/Logout",new() { Form=logoutForm });
        Assert.Equal(401,(await user.APIRequest.GetAsync(resource+"/arquivo?versionId="+version)).Status);
        Assert.Equal(401,(await user.APIRequest.GetAsync(app+"/DemaisSetores/ConsultaIntegrada/"+renewedPublic+"/arquivo?versionId="+version)).Status);
        stages.Add("logout-nega-sessao-anterior");
        await page.GotoAsync(entry);await Login(page);await page.WaitForURLAsync("**/ConsultaIntegrada/**");
        await page.GotoAsync(app+"/DemaisSetores/Parametrizar");
        await page.SelectOptionAsync("form[action$='DesativarIntegracao'] select[name='clientId']","partner-gate");
        await page.Locator("form[action$='DesativarIntegracao'] button").ClickAsync();
        foreach(var suffix in new[] { "", "/estado","/arquivo?versionId="+version,"/texto?versionId="+version })
        {
            var response=await partnerContext.APIRequest.GetAsync(app+"/DemaisSetores/ConsultaIntegrada/"+currentPublic+suffix);
            Assert.Equal(401,response.Status);
            Assert.Equal(401,(await partnerContext.APIRequest.GetAsync(app+"/DemaisSetores/ConsultaIntegrada/"+secondPublic+suffix)).Status);
        }
        var searchForm=partnerContext.APIRequest.CreateFormData();
        searchForm.Set("term","teste");
        var search=await partnerContext.APIRequest.PostAsync(app+"/DemaisSetores/ConsultaIntegrada/"+currentPublic+"/pesquisa",new() { Form=searchForm });
        Assert.Equal(401,search.Status);
        stages.Add("revogacao-pagina-pdf-texto-estado-pesquisa");
        summary.Completed=true;
    }

    private static async Task Login(IPage page)
    {
        await page.FillAsync("[name='Email']","sector@example.test");
        await page.FillAsync("[name='Password']","Sector-Test-2026!");
        await page.Locator("#loginForm button[type='submit']").ClickAsync();
    }
    private static async Task RestrictThirdParty(ICDPSession session)
    {
        await session.SendAsync("Network.enable");
        // Parameters verified against the protocol shipped with Playwright 1.54/Chromium.
        await session.SendAsync("Network.setCookieControls",new Dictionary<string,object> { ["enableThirdPartyCookieRestriction"]=true,
            ["disableThirdPartyCookieMetadata"]=true,["disableThirdPartyCookieHeuristics"]=true });
    }
    private static async Task ExpireFixtureSession(Guid publicId)
    {
        var start=new System.Diagnostics.ProcessStartInfo("docker") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
        foreach(var arg in new[] { "exec","inovaged-sector-validation","psql","-X","-q","-v","ON_ERROR_STOP=1","-U","postgres","-d","sector_clean","-c",
            "update ged.sector_open_session set expires_at=now()-interval '1 second' where tenant_id='50000000-0000-0000-0000-000000000001' and public_id='"+publicId.ToString("D")+"';" })start.ArgumentList.Add(arg);
        using var process=System.Diagnostics.Process.Start(start)!;
        var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();await output;await errors;
        Assert.Equal(0,process.ExitCode);
    }
    private static string FindRoot()
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory is not null && !File.Exists(Path.Combine(directory.FullName,"InovaGed.sln")))directory=directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
    private sealed class GateEvidence(string directory,List<string> stages) : IDisposable
    {
        public bool Completed { get; set; }
        public void Dispose()=>File.WriteAllText(Path.Combine(directory,"gate-results.json"),JsonSerializer.Serialize(new
        {
            status=Completed?"passed":"failed",stages,
            visualReview="CSS zoom is a layout simulation, not browser zoom. Native PDF paint requires separate visual acceptance."
        },new JsonSerializerOptions { WriteIndented=true }));
    }
}
