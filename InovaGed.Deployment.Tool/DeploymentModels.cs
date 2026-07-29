using System.Text.Json.Serialization;
namespace InovaGed.Deployment;
public sealed record ReleaseManifest
{
 public string SchemaVersion { get; init; }="1.0"; public string Product { get; init; }="InovaGED"; public string ApplicationVersion { get; init; }="";
 public string CommitSha { get; init; }=""; public string CommitShortSha { get; init; }=""; public string Branch { get; init; }=""; public string BuildNumber { get; init; }="";
 public DateTimeOffset BuiltAtUtc { get; init; }; public string TargetFramework { get; init; }="net8.0"; public string RuntimeMode { get; init; }="framework-dependent";
 public string RuntimeIdentifier { get; init; }="win-x64"; public string HostingModel { get; init; }="inprocess"; public string EnvironmentDoctorVersion { get; init; }="";
 public string DatabaseMigratorVersion { get; init; }=""; public string RequiredDotnetRuntime { get; init; }="8.0"; public string RequiredAspNetCoreRuntime { get; init; }="8.0";
 public string[] RequiredModules { get; init; }=[]; public string MigrationManifestChecksum { get; init; }=""; public string MinimumSchemaVersion { get; init; }="";
 public string MaximumCompatibleSchemaVersion { get; init; }=""; public string PackageChecksum { get; init; }=""; public ManifestFile[] Files { get; init; }=[];
 public HealthEndpoints HealthEndpoints { get; init; }=new();
}
public sealed record ManifestFile(string Path,string Sha256,long Size);
public sealed record HealthEndpoints { public string Live {get;init;}="/health/live"; public string Ready {get;init;}="/health/ready"; }
public enum DeploymentSafety { Additive, RequiresMaintenance, DestructiveBlocked, ManualOnly }
public static class RollbackCompatibility { public static bool IsCompatible(string current,string minimum,string maximum) => Version.TryParse(current,out var c)&&Version.TryParse(minimum,out var min)&&Version.TryParse(maximum,out var max)&&c>=min&&c<=max; }
