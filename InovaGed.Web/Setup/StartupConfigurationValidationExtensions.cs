using InovaGed.Application.SystemHealth;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;


namespace InovaGed.Web.Setup;

public static class StartupConfigurationValidationExtensions
{
    public static void ValidateInovaGedStartupConfiguration(
        this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var validator = app.Services.GetRequiredService<
            IStartupConfigurationValidator>();

        var checks = validator.Validate();

        foreach (var check in checks)
        {
            var level = check.Severity switch
            {
                StartupConfigurationSeverity.Critical =>
                    LogLevel.Critical,

                StartupConfigurationSeverity.Warning =>
                    LogLevel.Warning,

                _ => LogLevel.Information
            };

            app.Logger.Log(
                level,
                "STARTUP_CONFIGURATION " +
                "Item={Item} Status={Status} " +
                "Severity={Severity} Value={Value} " +
                "Source={Source} Environment={Environment} " +
                "Recommendation={Recommendation}",
                check.Item,
                check.Status,
                check.Severity,
                check.MaskedValue,
                check.Source,
                check.Environment,
                check.Recommendation);
        }

        var criticalChecks = checks
            .Where(x =>
                x.Severity ==
                StartupConfigurationSeverity.Critical)
            .ToArray();

        if (app.Environment.IsProduction() &&
            criticalChecks.Length > 0)
        {
            throw new InvalidOperationException(
                $"Inicialização bloqueada: " +
                $"{criticalChecks.Length} configuração(ões) " +
                "crítica(s) foram encontradas. " +
                "Consulte o log de inicialização.");
        }
    }
}
