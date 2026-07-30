using Xunit;

namespace InovaGed.UiTests;

public sealed class PlaywrightCompilationContractTests
{
    [Fact]
    public void Browser_matrix_uses_supported_dotnet_visual_snapshot_apis()
    {
        var source = File.ReadAllText(FindBrowserMatrix());
        Assert.DoesNotContain("ToHave" + "ScreenshotAsync", source);
        Assert.DoesNotContain("PageAssertions" + "ToHaveScreenshotOptions", source);
        Assert.DoesNotContain("EvaluateAll" + "Async", source);
        Assert.Contains("ScreenshotAsync", source);
        Assert.Contains("VisualSnapshotAssert.MatchAsync", source);
    }

    private static string FindBrowserMatrix()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "InovaGed.sln")))
            directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root was not found."),
            "InovaGed.UiTests", "BrowserTestMatrix.cs");
    }
}
