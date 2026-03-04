using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using InovaGed.Application.Common.Database;

namespace InovaGed.Web.Controllers;

public class AuthoritySourcesController : GedControllerBase
{
    public AuthoritySourcesController(IDbConnectionFactory dbFactory) : base(dbFactory) { }

    // GET /AuthoritySources
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        using var db = await OpenAsync();

        var sources = (await db.QueryAsync(@"
select id, name, kind, url, is_active, created_at, updated_at
from ged.authority_source
where tenant_id = @tid and reg_status = 'A'
order by is_active desc, name;", new { tid = TenantId })).ToList();

        var (expectedHash, computedHash) = await GetIntegrityAsync(db);

        ViewBag.ExpectedHash = expectedHash;
        ViewBag.ComputedHash = computedHash;
        ViewBag.IntegrityOk = !string.IsNullOrWhiteSpace(expectedHash) && expectedHash == computedHash;

        return View(sources);
    }

    // GET /AuthoritySources/Create
    [HttpGet]
    public IActionResult Create() => View("Edit", new AuthoritySourceVm());

    // GET /AuthoritySources/Edit?id=...
    [HttpGet]
    public async Task<IActionResult> Edit(Guid id)
    {
        using var db = await OpenAsync();

        var vm = await db.QuerySingleOrDefaultAsync<AuthoritySourceVm>(@"
select id, name, kind, url, content_pem as ContentPem, is_active as IsActive
from ged.authority_source
where tenant_id=@tid and id=@id and reg_status='A';", new { tid = TenantId, id });

        if (vm == null) return NotFound();
        return View(vm);
    }

    // POST /AuthoritySources/Save
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(AuthoritySourceVm vm)
    {
        if (string.IsNullOrWhiteSpace(vm.Name) || string.IsNullOrWhiteSpace(vm.Kind))
        {
            ModelState.AddModelError("", "Nome e Tipo (Kind) são obrigatórios.");
            return View("Edit", vm);
        }

        using var db = await OpenAsync();

        if (vm.Id == Guid.Empty)
        {
            vm.Id = Guid.NewGuid();

            await db.ExecuteAsync(@"
insert into ged.authority_source
(id, tenant_id, name, kind, url, content_pem, is_active, created_by)
values
(@id, @tid, @name, @kind, @url, @pem, @active, @uid);",
            new
            {
                id = vm.Id,
                tid = TenantId,
                name = vm.Name.Trim(),
                kind = vm.Kind.Trim(),
                url = string.IsNullOrWhiteSpace(vm.Url) ? null : vm.Url.Trim(),
                pem = string.IsNullOrWhiteSpace(vm.ContentPem) ? null : vm.ContentPem.Trim(),
                active = vm.IsActive,
                uid = CurrentUserIdOrNull()
            });

            await AddAudit(db, vm.Id, "CREATE", oldHash: null, newHash: await ComputeSourcesHash(db));
        }
        else
        {
            // pega hash antes
            var oldHash = await ComputeSourcesHash(db);

            await db.ExecuteAsync(@"
update ged.authority_source
set name=@name,
    kind=@kind,
    url=@url,
    content_pem=@pem,
    is_active=@active,
    updated_at=now(),
    updated_by=@uid
where tenant_id=@tid and id=@id and reg_status='A';",
            new
            {
                id = vm.Id,
                tid = TenantId,
                name = vm.Name.Trim(),
                kind = vm.Kind.Trim(),
                url = string.IsNullOrWhiteSpace(vm.Url) ? null : vm.Url.Trim(),
                pem = string.IsNullOrWhiteSpace(vm.ContentPem) ? null : vm.ContentPem.Trim(),
                active = vm.IsActive,
                uid = CurrentUserIdOrNull()
            });

            var newHash = await ComputeSourcesHash(db);
            await AddAudit(db, vm.Id, "UPDATE", oldHash, newHash);
        }

        // Atualiza hash “oficial” armazenado (integridade)
        await PersistIntegrityHash(db, await ComputeSourcesHash(db));

        return RedirectToAction(nameof(Index));
    }

    // POST /AuthoritySources/ToggleActive
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(Guid id, bool enable)
    {
        using var db = await OpenAsync();

        var oldHash = await ComputeSourcesHash(db);

        await db.ExecuteAsync(@"
update ged.authority_source
set is_active=@enable,
    updated_at=now(),
    updated_by=@uid
where tenant_id=@tid and id=@id and reg_status='A';",
        new { tid = TenantId, id, enable, uid = CurrentUserIdOrNull() });

        var newHash = await ComputeSourcesHash(db);
        await AddAudit(db, id, enable ? "ENABLE" : "DISABLE", oldHash, newHash);

        await PersistIntegrityHash(db, newHash);

        return RedirectToAction(nameof(Index));
    }

    // POST /AuthoritySources/RecomputeIntegrity (para demonstrar PoC)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecomputeIntegrity()
    {
        using var db = await OpenAsync();

        var newHash = await ComputeSourcesHash(db);
        await PersistIntegrityHash(db, newHash);

        await AddAudit(db, null, "RECOMPUTE_INTEGRITY", oldHash: null, newHash: newHash);

        return RedirectToAction(nameof(Index));
    }

    // ===== Helpers =====

    private Guid? CurrentUserIdOrNull()
    {
        // ajuste conforme seu auth (claim sub/nameidentifier etc.)
        var v = User?.Claims?.FirstOrDefault(c =>
            c.Type.EndsWith("nameidentifier", StringComparison.OrdinalIgnoreCase) ||
            c.Type.Equals("sub", StringComparison.OrdinalIgnoreCase))?.Value;

        return Guid.TryParse(v, out var id) ? id : null;
    }

    private async Task<(string? expected, string computed)> GetIntegrityAsync(System.Data.IDbConnection db)
    {
        var expected = await db.ExecuteScalarAsync<string?>(@"
select integrity_hash
from ged.authority_source_integrity
where tenant_id=@tid and reg_status='A'
order by computed_at desc
limit 1;", new { tid = TenantId });

        var computed = await ComputeSourcesHash(db);

        if (!string.IsNullOrWhiteSpace(expected) && expected != computed)
        {
            // Registra falha (item 12)
            await AddAudit(db, null, "INTEGRITY_FAIL", expected, computed);
        }

        return (expected, computed);
    }

    private async Task<string> ComputeSourcesHash(System.Data.IDbConnection db)
    {
        var rows = await db.QueryAsync(@"
select id, name, kind, coalesce(url,'') as url, coalesce(content_pem,'') as pem, is_active
from ged.authority_source
where tenant_id=@tid and reg_status='A'
order by id;", new { tid = TenantId });

        var sb = new StringBuilder();
        foreach (var r in rows)
        {
            sb.Append(r.id).Append('|')
              .Append(r.name).Append('|')
              .Append(r.kind).Append('|')
              .Append(r.url).Append('|')
              .Append(r.pem).Append('|')
              .Append(r.is_active)
              .Append('\n');
        }

        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes); // SHA256 em HEX
    }

    private async Task PersistIntegrityHash(System.Data.IDbConnection db, string hash)
    {
        await db.ExecuteAsync(@"
insert into ged.authority_source_integrity (tenant_id, integrity_hash, computed_at, computed_by)
values (@tid, @hash, now(), @uid)
on conflict (tenant_id)
do update set integrity_hash=excluded.integrity_hash, computed_at=excluded.computed_at, computed_by=excluded.computed_by;",
        new { tid = TenantId, hash, uid = CurrentUserIdOrNull() });
    }

    private async Task AddAudit(System.Data.IDbConnection db, Guid? sourceId, string action, string? oldHash, string? newHash)
    {
        await db.ExecuteAsync(@"
insert into ged.authority_source_audit
(id, tenant_id, source_id, action, details, old_hash, new_hash, created_at, created_by, ip_address, user_agent)
values
(gen_random_uuid(), @tid, @sid, @action, @details, @oldHash, @newHash, now(), @uid, @ip, @ua);",
        new
        {
            tid = TenantId,
            sid = sourceId,
            action = action,
            details = "Authority sources change / integrity control",
            oldHash = oldHash,
            newHash = newHash,
            uid = CurrentUserIdOrNull(),
            ip = HttpContext.Connection.RemoteIpAddress?.ToString(),
            ua = Request.Headers.UserAgent.ToString()
        });
    }
}

public class AuthoritySourceVm
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string? Kind { get; set; } // ex: "CA_ROOT", "CA_INTERMEDIATE", "OCSP", "CRL", "POLICY"
    public string? Url { get; set; }
    public string? ContentPem { get; set; } // opcional (cert/policy em PEM)
    public bool IsActive { get; set; } = true;
}