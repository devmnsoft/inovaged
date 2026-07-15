using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Common.Storage;
using InovaGed.Application.Documents;
using InovaGed.Application.DocumentGuardian;
using InovaGed.Application.Ged.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace InovaGed.Infrastructure;

public sealed class InovaGedDependencyHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _services;

    public InovaGedDependencyHealthCheck(IServiceProvider services) => _services = services;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var scope = _services.CreateScope();
        var provider = scope.ServiceProvider;
        var data = new Dictionary<string, object>();
        var missing = new List<string>();

        Check<IDbConnectionFactory>(provider, data, missing, "database");
        Check<IFileStorage>(provider, data, missing, "storage");
        Check<IAuditWriter>(provider, data, missing, "audit");
        Check<DocumentAppService>(provider, data, missing, "documents");
        Check<IDocumentMoveService>(provider, data, missing, "document-move");
        Check<IDocumentGuardianService>(provider, data, missing, "guardian");

        return Task.FromResult(missing.Count == 0
            ? HealthCheckResult.Healthy("Dependências centrais registradas.", data)
            : HealthCheckResult.Unhealthy("Há dependências centrais não registradas.", data: data));
    }

    private static void Check<T>(IServiceProvider provider, IDictionary<string, object> data, ICollection<string> missing, string module)
        where T : notnull
    {
        var service = provider.GetService<T>();
        var ok = service is not null;
        data[module] = ok ? "registered" : "missing";
        if (!ok) missing.Add(typeof(T).FullName ?? typeof(T).Name);
    }
}
