using System.Runtime.InteropServices;
using InovaGed.Application.EnvironmentDiagnostics;
using BclEnvironment = global::System.Environment;

namespace InovaGed.Infrastructure.EnvironmentDiagnostics;

public sealed class SystemEnvironmentContext : IEnvironmentContext
{
    public string CurrentDirectory => BclEnvironment.CurrentDirectory;
    public string BaseDirectory => AppContext.BaseDirectory;
    public string MachineName => BclEnvironment.MachineName;
    public string OperatingSystemDescription => RuntimeInformation.OSDescription;
    public Architecture ProcessArchitecture => RuntimeInformation.ProcessArchitecture;
    public Architecture OperatingSystemArchitecture => RuntimeInformation.OSArchitecture;
    public bool IsWindows => OperatingSystem.IsWindows();
    public bool IsLinux => OperatingSystem.IsLinux();
    public bool IsMacOS => OperatingSystem.IsMacOS();
    public string? GetEnvironmentVariable(string name) => BclEnvironment.GetEnvironmentVariable(name);
}
