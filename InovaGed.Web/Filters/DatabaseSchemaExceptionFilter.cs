using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Npgsql;
using InovaGed.Application.SystemHealth.Incidents;

namespace InovaGed.Web.Filters;

public sealed class DatabaseSchemaExceptionFilter : IAsyncExceptionFilter
{
    private const string FriendlyMessage = "A estrutura de banco necessária para esta funcionalidade ainda não está aplicada. Acesse /DatabaseReadiness para aplicar as migrations pendentes.";
    private const string ErrorStep = "DatabaseSchema";
    private const string MigrationScript = "database/apply_all_required_migrations.sql";

    private readonly ILogger<DatabaseSchemaExceptionFilter> _logger;
    private readonly IHostEnvironment _environment;
    private readonly IExceptionClassifier _classifier;
    private readonly ISystemIncidentService _incidents;

    public DatabaseSchemaExceptionFilter(
        ILogger<DatabaseSchemaExceptionFilter> logger,
        IHostEnvironment environment,
        IExceptionClassifier classifier,
        ISystemIncidentService incidents)
    {
        _logger = logger;
        _environment = environment;
        _classifier = classifier;
        _incidents = incidents;
    }

    public async Task OnExceptionAsync(ExceptionContext context)
    {
        if (FindSchemaException(context.Exception) is not { } pg)
            return;

        var (controllerName, actionName) = GetControllerAction(context);
        var correlationId = context.HttpContext.TraceIdentifier;
        var requestPath = context.HttpContext.Request.Path.Value;
        var classification = _classifier.Classify(pg, context.HttpContext);
        try
        {
            await _incidents.RegisterAsync(new(
                classification.IncidentType, classification.Severity, classification.Title,
                Message: "A estrutura de banco está desatualizada.", TechnicalMessage: pg.MessageText,
                RecommendedAction: classification.RecommendedAction, RecommendedScript: MigrationScript,
                CorrelationId: correlationId, Controller: controllerName, Action: actionName, Path: requestPath,
                HttpMethod: context.HttpContext.Request.Method, SqlState: pg.SqlState,
                DatabaseObject: classification.DatabaseObject, ExceptionType: pg.GetType().FullName,
                StackTrace: pg.StackTrace), context.HttpContext.RequestAborted);
        }
        catch (Exception registrationError)
        {
            _logger.LogError(registrationError, "Não foi possível persistir o incidente de schema. CorrelationId={CorrelationId}", correlationId);
        }

        _logger.LogError(
            pg,
            "Erro de schema PostgreSQL. SqlState={SqlState} Message={MessageText} Controller={Controller} Action={Action} Path={Path} CorrelationId={CorrelationId}",
            pg.SqlState,
            pg.MessageText,
            controllerName,
            actionName,
            requestPath,
            correlationId);

        var schemaObject = GetSchemaObject(pg);
        var isDocumentQualitySchemaPending = IsDocumentQualitySchemaException(pg);
        var friendlyMessage = isDocumentQualitySchemaPending
            ? "A funcionalidade de Qualidade Documental foi ativada, mas as tabelas ainda não foram criadas."
            : string.IsNullOrWhiteSpace(schemaObject)
                ? FriendlyMessage
                : $"A estrutura {schemaObject} ainda não existe ou está desatualizada. Acesse /DatabaseReadiness para aplicar as migrations pendentes.";

        if (IsAjaxOrApi(context.HttpContext.Request))
        {
            context.Result = new ObjectResult(new
            {
                success = false,
                message = friendlyMessage,
                errorStep = ErrorStep,
                sqlState = pg.SqlState,
                correlationId,
                schemaObject,
                controller = controllerName,
                action = actionName,
                route = requestPath,
                migration = MigrationScript,
                schemaHealthUrl = "/SchemaHealth",
                databaseReadinessUrl = "/DatabaseReadiness"
            })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };

            context.ExceptionHandled = true;
            return;
        }

        context.Result = new ViewResult
        {
            ViewName = "~/Views/Shared/DatabaseSchemaError.cshtml",
            StatusCode = StatusCodes.Status500InternalServerError,
            ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(
                new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(),
                context.ModelState)
            {
                ["Title"] = isDocumentQualitySchemaPending ? "Schema de Qualidade Documental pendente" : "Banco de dados desatualizado",
                ["Message"] = friendlyMessage,
                ["SqlState"] = pg.SqlState,
                ["CorrelationId"] = correlationId,
                ["Controller"] = controllerName,
                ["Action"] = actionName,
                ["Path"] = requestPath,
                ["Migration"] = MigrationScript,
                ["SchemaObject"] = schemaObject,
                ["SchemaHealthUrl"] = "/SchemaHealth",
                ["DatabaseReadinessUrl"] = "/DatabaseReadiness",
                ["CopyCommand"] = isDocumentQualitySchemaPending ? @"psql ""$DATABASE_URL"" -f database/apply_all_required_migrations.sql" : null,
                ["Detail"] = _environment.IsDevelopment() ? pg.MessageText : null
            }
        };

        context.ExceptionHandled = true;
    }

    private static (string Controller, string Action) GetControllerAction(ExceptionContext context)
    {
        if (context.ActionDescriptor is ControllerActionDescriptor cad)
        {
            return (
                cad.ControllerName ?? "UnknownController",
                cad.ActionName ?? "UnknownAction");
        }

        var routeValues = context.RouteData.Values;

        var controller = routeValues.TryGetValue("controller", out var c)
            ? c?.ToString() ?? "UnknownController"
            : "UnknownController";

        var action = routeValues.TryGetValue("action", out var a)
            ? a?.ToString() ?? "UnknownAction"
            : "UnknownAction";

        return (controller, action);
    }

    private static bool IsDocumentQualitySchemaException(PostgresException pg)
    {
        if (pg.SqlState != "42P01")
            return false;

        var text = $"{pg.MessageText} {pg.Detail} {pg.TableName} {pg.Where}";
        return text.Contains("document_quality_result", StringComparison.OrdinalIgnoreCase)
            || text.Contains("document_quality_run", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetSchemaObject(PostgresException pg)
    {
        var qualifiedTable = string.IsNullOrWhiteSpace(pg.TableName)
            ? null
            : string.IsNullOrWhiteSpace(pg.SchemaName) ? pg.TableName : $"{pg.SchemaName}.{pg.TableName}";
        if (!string.IsNullOrWhiteSpace(pg.ColumnName))
            return string.IsNullOrWhiteSpace(qualifiedTable) ? pg.ColumnName : $"{qualifiedTable}.{pg.ColumnName}";
        if (!string.IsNullOrWhiteSpace(qualifiedTable))
            return qualifiedTable;

        // PostgreSQL does not always populate TableName/ColumnName for parse-time errors.
        // MessageText is server-provided diagnostic text (not rendered as HTML).
        var marker = pg.SqlState == "42P01" ? "relation \"" : "column \"";
        var start = pg.MessageText.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        start += marker.Length;
        var end = pg.MessageText.IndexOf('"', start);
        return end > start ? pg.MessageText[start..end] : null;
    }

    private static PostgresException? FindSchemaException(Exception exception)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException!)
        {
            if (ex is PostgresException pg && pg.SqlState is "42703" or "42P01" or "42804")
                return pg;
        }

        return null;
    }

    private static bool IsAjaxOrApi(HttpRequest request)
    {
        return string.Equals(request.Headers.XRequestedWith, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
            || request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            || request.Headers.Accept.Any(h => h?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true);
    }
}
