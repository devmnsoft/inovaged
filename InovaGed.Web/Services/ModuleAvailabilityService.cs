using InovaGed.Web.Models.ReleaseReadiness;

namespace InovaGed.Web.Services;

public interface IModuleAvailabilityService
{
    ModuleAvailability Get(string moduleCode);
    bool IsEnabled(string moduleCode);
}

public sealed class ModuleAvailabilityService(IConfiguration configuration) : IModuleAvailabilityService
{
    public ModuleAvailability Get(string moduleCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleCode);
        var section = configuration.GetSection($"Modules:{moduleCode}");
        return new ModuleAvailability(
            section.GetValue("Enabled", true),
            section["Reason"],
            section["TechnicalForecast"]);
    }

    public bool IsEnabled(string moduleCode) => Get(moduleCode).Enabled;
}
