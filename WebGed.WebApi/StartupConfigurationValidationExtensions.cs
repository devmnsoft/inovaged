using InovaGed.Application.SystemHealth;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public static class StartupConfigurationValidationExtensions
{
    public static void ValidateInovaGedStartupConfiguration(this WebApplication app)
    {
        var validator = app.Services.GetRequiredService<IStartupConfigurationValidator>();
        var checks = validator.Validate();
        var critical = checks.Where(x => x.Severity == StartupConfigurationSeverity.Critical).ToArray();

        foreach (var check in checks)
        {
            app.Logger.Log(
                check.Severity == StartupConfigurationSeverity.Critical
                    ? LogLevel.Critical
                    : check.Severity == StartupConfigurationSeverity.Warning
                        ? LogLevel.Warning
                        : LogLevel.Information,
                "STARTUP_CONFIGURATION Item={Item} Status={Status} Severity={Severity} Value={Value} Recommendation={Recommendation} Environment={Environment}",
                check.Item,
                check.Status,
                check.Severity,
                check.MaskedValue,
                check.Recommendation,
                check.Environment);
        }

        if (critical.Length > 0 && app.Environment.IsProduction())
        {
            throw new InvalidOperationException($"Inicialização bloqueada: {critical.Length} configuração(ões) crítica(s).");
        }
    }
}
