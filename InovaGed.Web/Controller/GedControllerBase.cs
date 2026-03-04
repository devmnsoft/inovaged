using System.Data;
using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using InovaGed.Application.Common.Database;

namespace InovaGed.Web.Controllers;

public abstract class GedControllerBase : Controller
{
    protected readonly IDbConnectionFactory DbFactory;

    protected GedControllerBase(IDbConnectionFactory dbFactory)
    {
        DbFactory = dbFactory;
    }

    protected Guid TenantId
    {
        get
        {
            var claim = User?.FindFirst("tenant_id")?.Value;
            if (Guid.TryParse(claim, out var tid)) return tid;
            return Guid.Parse("00000000-0000-0000-0000-000000000001");
        }
    }

    protected Guid? UserId
    {
        get
        {
            var claim = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(claim, out var uid)) return uid;
            return null;
        }
    }

    protected string? UserNameSafe => User?.Identity?.Name;

    protected async Task<IDbConnection> OpenAsync()
    {
        var conn = DbFactory.CreateConnection();
        if (conn.State != ConnectionState.Open)
            conn.Open();
        return conn;
    }

    protected async Task<bool> HasTableAsync(IDbConnection db, string schema, string table)
    {
        var full = $"{schema}.{table}";
        return await db.ExecuteScalarAsync<bool>("select to_regclass(@p) is not null;", new { p = full });
    }
}