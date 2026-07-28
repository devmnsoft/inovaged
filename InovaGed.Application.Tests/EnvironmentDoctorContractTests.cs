using System.Text.RegularExpressions;
using Xunit;

namespace InovaGed.Application.Tests;

public sealed class GlobalJsonContractTests
{
    [Fact] public void UsesStableNet8FeatureBand() { var json=File.ReadAllText(Root("global.json")); Assert.Contains("\"version\": \"8.0.100\"",json); Assert.Contains("\"rollForward\": \"latestFeature\"",json); Assert.Contains("\"allowPrerelease\": false",json); }
    internal static string Root(string path) { var d=new DirectoryInfo(AppContext.BaseDirectory); while(d is not null && !File.Exists(Path.Combine(d.FullName,"InovaGed.sln"))) d=d.Parent; return Path.Combine(d!.FullName,path); }
}
public sealed class TargetFrameworkContractTests
{
    [Fact] public void EveryProjectTargetsNet8() { var root=Path.GetDirectoryName(GlobalJsonContractTests.Root("InovaGed.sln"))!; foreach(var file in Directory.GetFiles(root,"*.csproj",SearchOption.AllDirectories).Where(p=>!p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))) { var xml=File.ReadAllText(file); Assert.Matches("<TargetFramework>net8\\.0</TargetFramework>",xml); Assert.DoesNotContain("net9.0",xml); Assert.DoesNotContain("net10.0",xml); } }
}
public sealed class PackageVersionAlignmentTests
{
    [Fact] public void ProjectReferencesDoNotDeclareVersions() { var root=Path.GetDirectoryName(GlobalJsonContractTests.Root("InovaGed.sln"))!; foreach(var file in Directory.GetFiles(root,"*.csproj",SearchOption.AllDirectories)) Assert.DoesNotMatch(new Regex("<PackageReference[^>]+Version=",RegexOptions.NonBacktracking),File.ReadAllText(file)); }
}
public sealed class DotnetSdkVerificationTests
{
    [Fact] public void ReportedSdk9FixtureHasActionableFailureContract() { var source=File.ReadAllText(GlobalJsonContractTests.Root("InovaGed.Environment.Doctor/EnvironmentDoctor.cs")); Assert.Contains("DOTNET_SDK_8_NOT_FOUND",source); Assert.Contains("Instale o .NET SDK 8",source); Assert.Contains("true, (\"selectedMajor\"",source); }
}
public sealed class EnvironmentDoctorSecurityTests
{
    [Fact] public void DoctorDoesNotExposeConnectionStrings() { var source=File.ReadAllText(GlobalJsonContractTests.Root("InovaGed.Environment.Doctor/EnvironmentDoctor.cs")); Assert.DoesNotContain("ConnectionString",source); Assert.Contains("MaskPath",source); }
}
public sealed class EnvironmentDoctorExitCodeTests
{
    [Fact] public void CliDefinesAllExitCodes() { var source=File.ReadAllText(GlobalJsonContractTests.Root("InovaGed.Environment.Doctor/Program.cs")); foreach(var code in Enumerable.Range(0,5)) Assert.Contains($"return {code};",source); }
}
