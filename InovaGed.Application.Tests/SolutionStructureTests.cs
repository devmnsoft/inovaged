using System.Diagnostics;
using Xunit;

namespace InovaGed.Application.Tests;

public sealed class SolutionStructureTests
{
    private static readonly string[] ExpectedProjects =
    [
        "InovaGed.Domain/InovaGed.Domain.csproj",
        "InovaGed.Application/InovaGed.Application.csproj",
        "InovaGed.Infrastructure/InovaGed.Infrastructure.csproj",
        "InovaGed.Web/InovaGed.Web.csproj",
        "WebGed.WebApi/WebGed.WebApi.csproj",
        "InovaGed.Operations.Worker/InovaGed.Operations.Worker.csproj",
        "InovaGed.Portability.Verifier/InovaGed.Portability.Verifier.csproj",
        "InovaGed.Signing.Agent/InovaGed.Signing.Agent.csproj",
        "InovaGed.Application.Tests/InovaGed.Application.Tests.csproj",
        "InovaGed.Signing.Agent.Tests/InovaGed.Signing.Agent.Tests.csproj",
        "InovaGed.Signing.EndToEndTests/InovaGed.Signing.EndToEndTests.csproj"
    ];

    [Fact]
    public async Task DotnetSlnList_ContainsEachProjectExactlyOnce()
    {
        var repo = FindRepositoryRoot();
        using var process = Process.Start(new ProcessStartInfo("dotnet", "sln InovaGed.sln list")
        {
            WorkingDirectory = repo,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("dotnet sln could not be started.");
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(process.ExitCode == 0, output + error);
        var normalized = output.Replace('\\', '/');
        foreach (var project in ExpectedProjects)
            Assert.Equal(1, CountOccurrences(normalized, project));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "InovaGed.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("InovaGed.sln not found.");
    }
}
