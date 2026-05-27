using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

public abstract class AppControllerBase : Controller
{
    protected IActionResult JsonSuccess(string message, object? data = null)
        => Ok(new
        {
            success = true,
            message,
            data = data ?? new { },
            correlationId = HttpContext.TraceIdentifier
        });

    protected IActionResult JsonError(
        string message,
        string errorStep,
        string? errorLog = null,
        bool canRetry = false,
        int statusCode = 400)
        => StatusCode(statusCode, new
        {
            success = false,
            message,
            errorStep,
            errorLog,
            canRetry,
            correlationId = HttpContext.TraceIdentifier
        });
}
