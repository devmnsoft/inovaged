using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace InovaGed.Web.Controllers;

/// <summary>Last-resort boundary so optional SmartSearch persistence failures never render an HTML 500 page.</summary>
public sealed class SmartSearchExceptionFilter : IAsyncExceptionFilter
{
    private readonly ILogger<SmartSearchExceptionFilter> _logger;

    public SmartSearchExceptionFilter(ILogger<SmartSearchExceptionFilter> logger) => _logger = logger;

    public Task OnExceptionAsync(ExceptionContext context)
    {
        var correlationId = context.HttpContext.TraceIdentifier;
        _logger.LogError(context.Exception,
            "Falha não tratada no SmartSearch. Action={Action} CorrelationId={CorrelationId}",
            context.RouteData.Values["action"], correlationId);
        context.Result = new ObjectResult(new
        {
            success = false,
            message = "O SmartSearch está temporariamente indisponível. Tente novamente em instantes.",
            correlationId
        }) { StatusCode = StatusCodes.Status503ServiceUnavailable };
        context.ExceptionHandled = true;
        return Task.CompletedTask;
    }
}
