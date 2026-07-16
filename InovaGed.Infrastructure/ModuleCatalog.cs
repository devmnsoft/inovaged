using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace InovaGed.Infrastructure;

public sealed record InfrastructureModuleDescriptor(
    string Name,
    bool Enabled,
    IReadOnlyList<string> Dependencies,
    bool ConfigurationValid,
    HealthStatus Health,
    string Version,
    string? LastFailure);

public interface IInfrastructureModuleCatalog
{
    IReadOnlyList<InfrastructureModuleDescriptor> GetModules();
}

internal sealed class InfrastructureModuleCatalog : IInfrastructureModuleCatalog
{
    private readonly IReadOnlyList<InfrastructureModuleDescriptor> _modules;

    public InfrastructureModuleCatalog(IEnumerable<InfrastructureModuleDescriptor> modules)
    {
        _modules = modules.OrderBy(module => module.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlyList<InfrastructureModuleDescriptor> GetModules() => _modules;
}
