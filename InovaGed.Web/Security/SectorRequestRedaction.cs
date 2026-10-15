using Microsoft.AspNetCore.Routing;

namespace InovaGed.Web.Security;

public static class SectorRequestRedaction
{
    public static bool Applies(HttpContext context)
        => context.Request.Path.StartsWithSegments("/DemaisSetores", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.StartsWithSegments("/api/integracoes/demais-setores", StringComparison.OrdinalIgnoreCase);

    public static string Path(HttpContext context)
        => Applies(context)
            ? (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "/DemaisSetores/[contexto]"
            : context.Request.Path.Value ?? "";
}
