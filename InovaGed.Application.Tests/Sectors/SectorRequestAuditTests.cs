using System.Text.Json;
using InovaGed.Application.Audit;
using InovaGed.Application.Identity;
using InovaGed.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace InovaGed.Application.Tests.Sectors;

public sealed class SectorRequestAuditTests
{
    [Theory]
    [InlineData("/DemaisSetores/Integrar", "/DemaisSetores/Integrar")]
    [InlineData("/DemaisSetores/Abrir/synthetic-token", "/DemaisSetores/Abrir/{code}")]
    [InlineData("/api/integracoes/demais-setores/aberturas", "/api/integracoes/demais-setores/aberturas")]
    public async Task Error_audits_keep_correlation_without_query_route_values_or_exception_input(string path,string template)
    {
        foreach (var fail in new[] { false,true })
        {
            var http = new DefaultHttpContext();
            http.Request.Path=path;
            http.Request.QueryString=new QueryString("?paciente=52998224725&segredo=synthetic-secret");
            http.Request.RouteValues["code"]="synthetic-token";
            http.Request.RouteValues["controller"]="DemaisSetores";
            http.TraceIdentifier="synthetic-correlation";
            http.SetEndpoint(new RouteEndpoint(_=>Task.CompletedTask,RoutePatternFactory.Parse(template),0,new EndpointMetadataCollection(),"Abrir"));
            var audit = new Capture();
            var middleware = new RequestAuditMiddleware(ctx=>
            {
                if (fail) throw new InvalidOperationException("52998224725 synthetic-secret synthetic-token");
                ctx.Response.StatusCode=403;
                return Task.CompletedTask;
            });
            if (fail) await Assert.ThrowsAsync<InvalidOperationException>(()=>middleware.Invoke(http,new User(),audit));
            else await middleware.Invoke(http,new User(),audit);
            var entry=Assert.Single(audit.Entries);
            Assert.Equal(template,entry.Path);
            Assert.Equal("synthetic-correlation",entry.CorrelationId);
            var serialized=JsonSerializer.Serialize(entry);
            Assert.DoesNotContain("52998224725",serialized);
            Assert.DoesNotContain("synthetic-secret",serialized);
            Assert.DoesNotContain("synthetic-token",serialized);
        }
    }

    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated=>false;
        public Guid TenantId=>Guid.Empty;
        public Guid UserId=>Guid.Empty;
        public string Email=>"";
        public IReadOnlyList<string> Roles=>[];
    }

    private sealed class Capture : IAppAuditLogService
    {
        public List<AppAuditLogEntry> Entries { get; }=[];
        public Task LogAsync(AppAuditLogEntry entry,CancellationToken ct=default) { Entries.Add(entry);return Task.CompletedTask; }
        public Task LogErrorAsync(Guid? tenantId,Guid? userId,string source,string message,Exception? exception,object? data=null,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task LogSecurityAsync(Guid? tenantId,Guid? userId,string action,string message,object? data=null,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task LogBusinessAsync(Guid? tenantId,Guid? userId,string action,string entityName,Guid? entityId,string message,object? data=null,string? entityKey=null,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task LogHttpAsync(Guid? tenantId,Guid? userId,string method,string path,int? statusCode,long elapsedMs,object? data=null,CancellationToken ct=default)=>throw new NotSupportedException();
    }
}
