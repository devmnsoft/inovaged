using System.Text.RegularExpressions;
using InovaGed.Application.EnvironmentDiagnostics;
using InovaGed.Environment.Doctor;
using InovaGed.Infrastructure.EnvironmentDiagnostics;
using Xunit;

namespace InovaGed.Application.Tests;

public sealed class SystemEnvironmentNamespaceCollisionTests
{
    [Fact]
    public void DoctorSourcesUseExplicitSystemEnvironmentAlias()
    {
        var directory = Path.GetDirectoryName(GlobalJsonContractTests.Root("InovaGed.Environment.Doctor/Program.cs"))!;
        var sources = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(File.ReadAllText).ToArray();
        Assert.Contains(sources, source => source.Contains("using BclEnvironment = global::System.Environment;", StringComparison.Ordinal));
        var collision = new Regex(@"(?<!Bcl)(?<!System\.)(?<!global::System\.)\bEnvironment\.(CurrentDirectory|NewLine|MachineName|ProcessPath|GetEnvironmentVariable)");
        Assert.DoesNotContain(sources, collision.IsMatch);
    }
}

public sealed class RepositoryFileLocatorTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"doctor-{Guid.NewGuid():N}");
    public RepositoryFileLocatorTests() => Directory.CreateDirectory(root);
    [Fact] public void FindsFromBaseAndParent() { var child=Directory.CreateDirectory(Path.Combine(root,"a","b")); var file=Path.Combine(root,"global.json"); File.WriteAllText(file,"{}"); Assert.Equal(file,EnvironmentDoctor.FindRepositoryFile("global.json",child.FullName,Path.GetTempPath())); }
    [Fact] public void FindsFromCurrentAndParent() { var child=Directory.CreateDirectory(Path.Combine(root,"a","b")); var file=Path.Combine(root,"InovaGed.sln"); File.WriteAllText(file,""); Assert.Equal(file,EnvironmentDoctor.FindRepositoryFile("InovaGed.sln",Path.Combine(root,"missing"),child.FullName)); }
    [Fact] public void MissingDirectoriesReturnNull() => Assert.Null(EnvironmentDoctor.FindRepositoryFile("global.json",Path.Combine(root,"missing"),Path.Combine(root,"other")));
    [Theory] [InlineData("")] [InlineData("../global.json")] [InlineData("unknown.txt")]
    public void RejectsUnsafeNames(string name) => Assert.Throws<ArgumentException>(() => EnvironmentDoctor.FindRepositoryFile(name,root,root));
    public void Dispose() => Directory.Delete(root,true);
}

public sealed class SafeMetadataSanitizerTests
{
    [Fact] public void RemovesSensitiveKeysAndMasksValues() { var sut=new SafeMetadataSanitizer(); var result=sut.Sanitize(new Dictionary<string,string?> { ["PaSsWoRd"]="secret", ["path"]="/home/runner/repo" }); Assert.DoesNotContain("PaSsWoRd",result.Keys); Assert.Equal("[PATH]",result["path"]); }
}

public sealed class SystemEnvironmentContextTests
{
    [Fact] public void ExposesRuntimeWithoutLeakingImplementation() { IEnvironmentContext sut=new SystemEnvironmentContext(); Assert.False(string.IsNullOrWhiteSpace(sut.CurrentDirectory)); Assert.True(sut.IsLinux || sut.IsWindows || sut.IsMacOS); }
}
