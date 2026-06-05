using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Npgsql;

namespace InovaGed.Web.Filters;

public sealed class DatabaseSchemaExceptionFilter : IExceptionFilter
{
    private const string FriendlyMessage = "Estrutura do banco de dados desatualizada. Execute as migrations do sistema.";
    private static readonly HashSet<string> CriticalControllers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ged",
        "HospitalDocuments",
        "SystemLogs",
        "UploadBatch",
        "UploadChunk"
    };

    private readonly ILogger<DatabaseSchemaExceptionFilter> _logger;

    public DatabaseSchemaExceptionFilter(ILogger<DatabaseSchemaExceptionFilter> logger)
    {
        _logger = logger;
    }

    public void OnException(ExceptionContext context)
    {
        if (FindSchemaException(context.Exception) is not { } pg)
            return;

        var controller = context.RouteData.Values["controller"]?.ToString();
        if (!CriticalControllers.Contains(controller ?? string.Empty))
            return;

        var correlationId = context.HttpContext.TraceIdentifier;
        _logger.LogError(pg,
            "Schema do banco desatualizado em tela crítica. Controller={Controller} Action={Action} SqlState={SqlState} Table={Table} Column={Column} Message={Message} MigrationSugerida={Migration} CorrelationId={CorrelationId}",
            controller,
            context.RouteData.Values["action"],
            pg.SqlState,
            pg.TableName,
            pg.ColumnName,
            pg.MessageText,
            "database/apply_all_required_migrations.sql",
            correlationId);

        if (IsAjaxOrApi(context.HttpContext.Request))
        {
            context.Result = new ObjectResult(new
            {
                success = false,
                message = FriendlyMessage,
                code = pg.SqlState,
                correlationId,
                migration = "database/apply_all_required_migrations.sql"
            })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable
            };
        }
        else
        {
            if (context.Controller is Controller mvcController)
                mvcController.TempData["Error"] = FriendlyMessage;

            context.Result = new ViewResult
            {
                ViewName = "~/Views/Shared/SchemaOutdated.cshtml",
                StatusCode = StatusCodes.Status503ServiceUnavailable,
                ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(
                    new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(),
                    context.ModelState)
                {
                    ["CorrelationId"] = correlationId,
                    ["SqlState"] = pg.SqlState,
                    ["Migration"] = "database/apply_all_required_migrations.sql"
                },
                TempData = (context.Controller as Controller)?.TempData
            };
        }

        context.ExceptionHandled = true;
    }

    private static PostgresException? FindSchemaException(Exception exception)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException!)
        {
            if (ex is PostgresException pg && pg.SqlState is PostgresErrorCodes.UndefinedColumn or PostgresErrorCodes.UndefinedTable)
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
