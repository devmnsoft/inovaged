using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace InovaGed.UiTests;

[Collection("Sector external fixture")]
public sealed class SectorClinicalGateBrowserTests
{
    [SectorPartnerGateFact]
    public async Task Clinical_action_is_explicit_and_disabled_policy_is_reported_without_fake_suggestions()
    {
        var app=Environment.GetEnvironmentVariable("INOVAGED_SECTOR_APP")??"https://localhost:7189";
        Assert.StartsWith("https://localhost:",app);
        using var playwright=await Playwright.CreateAsync();
        await using var browser=await playwright.Chromium.LaunchAsync(new(){Headless=true,Channel="chromium"});
        await using var user=await browser.NewContextAsync(new(){IgnoreHTTPSErrors=true,ViewportSize=new(){Width=1366,Height=900}});
        var page=await user.NewPageAsync();page.SetDefaultTimeout(30000);
        await page.GotoAsync(app+"/DemaisSetores/Consultar?patientId=50000000-0000-0000-0000-000000000004&encounterId=50000000-0000-0000-0000-000000000005");
        await page.FillAsync("[name='Email']","sector@example.test");await page.FillAsync("[name='Password']","Sector-Test-2026!");
        await page.Locator("#loginForm button[type='submit']").ClickAsync();
        var form=page.Locator("[data-ds-clinical-extraction]").Filter(new(){Has=page.Locator("input[name='linkId'][value='50000000-0000-0000-0000-000000000008']")});
        await form.Locator("..").Locator("summary").ClickAsync();
        var response=page.WaitForResponseAsync(x=>x.Url.EndsWith("/DemaisSetores/ExtrairItensClinicos")&&x.Request.Method=="POST");
        await form.Locator("button[type='submit']").ClickAsync();
        var denied=await response;Assert.Equal(422,denied.Status);
        var json=(await denied.JsonAsync())!.Value;Assert.Equal("FALHOU",json.GetProperty("state").GetString());
        await Assertions.Expect(form.Locator("[data-ds-clinical-result]")).ToContainTextAsync("política");
        Assert.False(await form.Locator("[data-ds-clinical-refresh]").IsVisibleAsync());
        await Assertions.Expect(form.Locator("button[type='submit']")).ToBeEnabledAsync();
        await Assertions.Expect(form.Locator("button[type='submit']")).ToHaveTextAsync("Sugerir itens clínicos com IA");
        var repeated=page.WaitForResponseAsync(x=>x.Url.EndsWith("/DemaisSetores/ExtrairItensClinicos")&&x.Request.Method=="POST");
        await form.Locator("button[type='submit']").ClickAsync();
        Assert.Equal(422,(await repeated).Status);
        await Assertions.Expect(form.Locator("[data-ds-clinical-result]")).ToContainTextAsync("política");
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root.Parent is not null&&!File.Exists(Path.Combine(root.FullName,"InovaGed.sln")))root=root.Parent;
        var directory=Path.Combine(root.FullName,"artifacts/sector-validation/clinical-ui");Directory.CreateDirectory(directory);
        foreach(var width in new[]{360,768,1366,1920})
        {
            await page.SetViewportSizeAsync(width,1000);await form.ScrollIntoViewIfNeededAsync();
            await page.ScreenshotAsync(new(){Path=Path.Combine(directory,$"clinical-disabled-{width}.png")});
        }
        await File.WriteAllTextAsync(Path.Combine(directory,"gate-results.json"),JsonSerializer.Serialize(new{status="passed",policyDisabled=true,noAutomaticConfirmation=true,providerNotHomologated=true}));
    }
}
