using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace InovaGed.UiTests;

public sealed class BrowserTestMatrix
{
    private static readonly SemaphoreSlim ManifestLock = new(1, 1);
    private static readonly string EvidenceRoot = FindEvidenceRoot();

    public static IEnumerable<object[]> PagesAndViewports()
    {
        var pages = new (string Name, string Path)[]
        {
            ("Login", "/Account/Login"), ("Dashboard", "/GedDashboard"),
            ("GED", "/Ged"), ("HospitalSearch", "/HospitalDocuments"),
            ("Administration", "/Administration"), ("Loans", "/Loans"),
            ("Protocols", "/Protocols"), ("Continuity", "/Continuity")
        };
        var viewports = new (int Width, int Height)[]
        {
            (390, 844), (768, 1024), (1366, 768), (1440, 900), (1920, 1080)
        };
        var profiles = new[] { "admin", "archivist", "hospital" };
        var index = 0;
        return pages.SelectMany(page => viewports.Select(viewport =>
            new object[] { profiles[index++ % profiles.Length], page.Name, page.Path, viewport.Width, viewport.Height })).ToArray();
    }

    [Theory]
    [MemberData(nameof(PagesAndViewports))]
    [Trait("Category", "UI")]
    public async Task Page_matches_reviewed_visual_baseline(string profile, string name, string path, int width, int height)
    {
        var baseUrl = Environment.GetEnvironmentVariable("INOVAGED_UI_BASE_URL");
        if (string.IsNullOrWhiteSpace(baseUrl)) return; // Browser suite is activated only by its isolated CI environment.

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var context = await browser.NewContextAsync(new()
        {
            BaseURL = baseUrl,
            ViewportSize = new() { Width = width, Height = height },
            Locale = "pt-BR",
            TimezoneId = "UTC",
            ReducedMotion = ReducedMotion.Reduce,
            RecordVideoDir = Path.Combine(EvidenceRoot, "Reports", "videos")
        });
        var page = await context.NewPageAsync();
        var consoleErrors = new List<string>();
        page.Console += (_, message) => { if (message.Type == "error") consoleErrors.Add(message.Text); };
        page.PageError += (_, error) => consoleErrors.Add(error);

        await LoginAsync(page, baseUrl, profile);
        var response = await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.NetworkIdle });
        Assert.NotNull(response);
        Assert.True(response!.Status < 500, $"{path} returned HTTP {response.Status}");
        await page.EvaluateAsync("document.fonts.ready");
        await page.Locator("[data-dynamic], time").EvaluateAllAsync("nodes => nodes.forEach(n => n.textContent = '30/07/2026 12:00')");

        var relativeScreenshot = Path.Combine("Screenshots", "actual", $"{Slug(profile)}-{Slug(name)}-{width}x{height}.png");
        var screenshot = Path.Combine(EvidenceRoot, relativeScreenshot);
        Directory.CreateDirectory(Path.GetDirectoryName(screenshot)!);
        await page.ScreenshotAsync(new() { Path = screenshot, FullPage = true, Animations = ScreenshotAnimations.Disabled });
        await Assertions.Expect(page).ToHaveScreenshotAsync(new PageAssertionsToHaveScreenshotOptions
        {
            FullPage = true,
            Animations = ScreenshotAnimations.Disabled,
            MaxDiffPixelRatio = name == "Login" ? 0.005 : 0.015
        });

        Assert.Empty(consoleErrors);
        await RecordAsync(profile, name, width, height, relativeScreenshot);
    }

    private static async Task LoginAsync(IPage page, string baseUrl, string profile)
    {
        await page.GotoAsync(new Uri(new Uri(baseUrl), "/Account/Login").ToString());
        var email = profile switch
        {
            "admin" => "admin@inovaged.local",
            "archivist" => "arquivistaophir@inovaged.local",
            "hospital" => "hospital@inovaged.local",
            _ => throw new InvalidOperationException("Unknown deterministic UI profile.")
        };
        var password = Environment.GetEnvironmentVariable("INOVAGED_UI_PASSWORD") ?? throw new InvalidOperationException("UI password is not configured.");
        await page.GetByLabel("E-mail ou CPF").FillAsync(email);
        await page.GetByLabel("Senha", new() { Exact = true }).FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Entrar" }).ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        Directory.CreateDirectory(Path.Combine(EvidenceRoot, "Reports"));
        await File.WriteAllTextAsync(Path.Combine(EvidenceRoot, "Reports", "login.occurred"), "real-login");
    }

    private static async Task RecordAsync(string profile, string page, int width, int height, string screenshot)
    {
        await ManifestLock.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.Combine(EvidenceRoot, "Reports"));
            var record = JsonSerializer.Serialize(new { test = $"{profile}-{page}-{width}x{height}", profile, page, viewport = $"{width}x{height}", screenshot, comparison = true });
            await File.AppendAllTextAsync(Path.Combine(EvidenceRoot, "Reports", "executions.jsonl"), record + Environment.NewLine);
        }
        finally { ManifestLock.Release(); }
    }

    private static string FindEvidenceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "InovaGed.sln"))) directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root was not found."), "InovaGed.UiTests");
    }

    private static string Slug(string value) => value.ToLowerInvariant().Replace(" ", "-");
}
