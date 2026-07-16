using System.Diagnostics;
using InovaGed.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace InovaGed.Application.Tests;

public sealed class SuspiciousRequestMiddlewareTests
{
    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("/Account/Login")]
    [InlineData("/Ged")]
    [InlineData("/DocumentGuardian")]
    [InlineData("/SystemHealth")]
    [InlineData("/Reports/EnvironmentSummary")]
    [InlineData("/Users/Manager")]
    [InlineData("/api/documents")]
    [InlineData("/css/site.css")]
    public async Task LegitimatePaths_AreAllowed(string path)
    {
        var called = false;
        var middleware = CreateMiddleware(_ => { called = true; return Task.CompletedTask; });
        var context = CreateContext(path);

        await middleware.InvokeAsync(context);

        Assert.True(called);
    }

    [Theory]
    [InlineData("/.env")]
    [InlineData("/.env.production")]
    [InlineData("/appsettings.json")]
    [InlineData("/appsettings.Production.json")]
    [InlineData("/.git/config")]
    [InlineData("/.svn/entries")]
    [InlineData("/node_modules/package/index.js")]
    [InlineData("/vendor/autoload.php")]
    [InlineData("/v2/_catalog")]
    [InlineData("/containers/json")]
    [InlineData("/shell.aspx")]
    [InlineData("/Telerik.Web.UI.DialogHandler.aspx")]
    [InlineData("/%2eenv")]
    [InlineData("/%2e%67%69%74/config")]
    [InlineData("/relat%C3%B3rios/.env")]
    public async Task SensitivePaths_AreBlockedWithoutCallingNext(string path)
    {
        var called = false;
        var middleware = CreateMiddleware(_ => { called = true; return Task.CompletedTask; });
        var context = CreateContext(path);

        await middleware.InvokeAsync(context);

        Assert.False(called);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task LongPath_Returns414_AndDoesNotCallNext()
    {
        var called = false;
        var middleware = CreateMiddleware(_ => { called = true; return Task.CompletedTask; }, maxPathLength: 32);
        var context = CreateContext("/" + new string('a', 128));

        await middleware.InvokeAsync(context);

        Assert.False(called);
        Assert.Equal(StatusCodes.Status414UriTooLong, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("/" )]
    [InlineData("/" + "................................................................................................................................................................................................................................................................................................................................")]
    [InlineData("/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////")]
    [InlineData("/%25%32%65env")]
    [InlineData("/%E0%A4%A")]
    [InlineData("/Documentos/ação/привет")]
    public async Task EdgeCasePaths_DoNotTimeoutOrThrow(string path)
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask, maxPathLength: 4096);
        var context = CreateContext(path);

        await middleware.InvokeAsync(context);

        Assert.True(context.Response.StatusCode is StatusCodes.Status200OK or StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task ThousandsOfPaths_RunPredictably()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask, maxPathLength: 4096);
        var stopwatch = Stopwatch.StartNew();

        for (var i = 0; i < 5_000; i++)
        {
            var context = CreateContext(i % 2 == 0 ? $"/api/documents/{i}" : $"/.git/{i}/config");
            await middleware.InvokeAsync(context);
        }

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Elapsed={stopwatch.Elapsed}");
    }

    private static SuspiciousRequestMiddleware CreateMiddleware(RequestDelegate next, int maxPathLength = 2048) =>
        new(next, NullLogger<SuspiciousRequestMiddleware>.Instance, Options.Create(new SuspiciousRequestOptions
        {
            MaxPathLength = maxPathLength,
            MaxLoggedPathLength = 64,
            MaxLoggedQueryLength = 32,
            MaxLoggedUserAgentLength = 32
        }));

    private static DefaultHttpContext CreateContext(string path)
    {
        var context = new DefaultHttpContext();
        context.TraceIdentifier = Guid.NewGuid().ToString("N");
        context.Response.Body = new MemoryStream();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;
        context.Request.Headers.UserAgent = "middleware-tests";
        return context;
    }
}
