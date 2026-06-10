using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Npgsql;

namespace InovaGed.Web.Filters;

public sealed class DatabaseSchemaExceptionFilter : IExceptionFilter
{
    private const string FriendlyMessage = "A estrutura do banco de dados está desatualizada. Execute as migrations do sistema.";
    private const string ErrorStep = "DatabaseSchema";
    private const string MigrationScript = "database/apply_all_required_migrations.sql";

    private readonly ILogger<DatabaseSchemaExceptionFilter> _logger;
    private readonly IHostEnvironment _environment;

    public DatabaseSchemaExceptionFilter(
        ILogger<DatabaseSchemaExceptionFilter> logger,
        IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    public void OnException(ExceptionContext context)
    {
        if (FindSchemaException(context.Exception) is not { } pg)
            return;

        var (controllerName, actionName) = GetControllerAction(context);
        var correlationId = context.HttpContext.TraceIdentifier;
        var requestPath = context.HttpContext.Request.Path.Value;

        _logger.LogError(
            pg,
            "Erro de schema PostgreSQL. SqlState={SqlState} Message={MessageText} Controller={Controller} Action={Action} Path={Path} CorrelationId={CorrelationId}",
            pg.SqlState,
            pg.MessageText,
            controllerName,
            actionName,
            requestPath,
            correlationId);

        var isDocumentQualitySchemaPending = IsDocumentQualitySchemaException(pg);
        var friendlyMessage = isDocumentQualitySchemaPending
            ? "A funcionalidade de Qualidade Documental foi ativada, mas as tabelas ainda não foram criadas."
            : FriendlyMessage;

        if (IsAjaxOrApi(context.HttpContext.Request))
        {
            context.Result = new ObjectResult(new
            {
                success = false,
                message = friendlyMessage,
                errorStep = ErrorStep,
                sqlState = pg.SqlState,
                correlationId,
                migration = MigrationScript
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
