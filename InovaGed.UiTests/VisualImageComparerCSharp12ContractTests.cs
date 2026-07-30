namespace InovaGed.UiTests;

public sealed class VisualImageComparerCSharp12ContractTests
{
    [Fact]
    public void Comparer_SeparatesAsyncIoFromSynchronousPixelRows()
    {
        var source = ReadRepositoryFile("InovaGed.UiTests", "VisualImageComparer.cs");
        var asyncBody = Slice(source, "public async Task<VisualComparisonResult> CompareAsync", "private static PixelComparisonResult ComparePixels");
        var pixelBody = Slice(source, "private static PixelComparisonResult ComparePixels", "private static VisualComparisonResult CreateDimensionMismatchResult");

        Assert.DoesNotContain("Span<", asyncBody, StringComparison.Ordinal);
        Assert.DoesNotContain("DangerousGetPixelRowMemory", asyncBody, StringComparison.Ordinal);
        Assert.DoesNotContain("async", pixelBody, StringComparison.Ordinal);
        Assert.Contains("DangerousGetPixelRowMemory", pixelBody, StringComparison.Ordinal);
        Assert.Contains(".Span", pixelBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_RemainsOnCSharp12()
    {
        var props = ReadRepositoryFile("Directory.Build.props");
        Assert.Contains("<LangVersion>12.0</LangVersion>", props, StringComparison.Ordinal);
        Assert.DoesNotContain("<LangVersion>13", props, StringComparison.Ordinal);
        Assert.DoesNotContain("<LangVersion>preview", props, StringComparison.OrdinalIgnoreCase);
    }

    private static string Slice(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && endIndex > startIndex);
        return source[startIndex..endIndex];
    }

    private static string ReadRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "InovaGed.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(new[] { directory!.FullName }.Concat(parts).ToArray()));
    }
}
