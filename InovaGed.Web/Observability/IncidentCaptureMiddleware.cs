using InovaGed.Application.SystemHealth.Incidents;
using Microsoft.AspNetCore.Routing;

namespace InovaGed.Web.Observability;

public sealed class IncidentCaptureMiddleware(RequestDelegate next,ILogger<IncidentCaptureMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context,IExceptionClassifier classifier,ISystemIncidentService incidents)
    {
        try { await next(context); }
        catch(Exception exception)
        {
            var classification=classifier.Classify(exception,context);
            if(exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested) throw;
            try
            {
                var route=context.GetRouteData()?.Values;
                await incidents.RegisterAsync(new(classification.IncidentType,classification.Severity,classification.Title,"Ocorreu uma falha técnica. Informe o CorrelationId ao suporte.",exception.Message,classification.RecommendedAction,null,null,context.TraceIdentifier,route?["controller"]?.ToString(),route?["action"]?.ToString(),context.Request.Path,context.Request.Method,classification.SqlState,classification.DatabaseObject,exception.GetType().FullName,exception.StackTrace),CancellationToken.None);
            }
            catch(Exception registrationError) { logger.LogError(registrationError,"Falha ao registrar incidente; a exceção original será preservada. CorrelationId={CorrelationId}",context.TraceIdentifier); }
            throw;
        }
    }
}
