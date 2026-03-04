using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using InovaGed.Application.Common.Database;

namespace InovaGed.Web.Controllers;

public class ImportExportController : GedControllerBase
{
    private readonly IHostEnvironment _env;

    public ImportExportController(IDbConnectionFactory dbFactory, IHostEnvironment env) : base(dbFactory)
    {
        _env = env;
    }

    // GET /ImportExport/Index
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        using var db = await OpenAsync();

        var docs = await db.QueryAsync(@"
select id, code, title, status, created_at
from ged.document
where tenant_id=@tid
order by created_at desc
limit 50;", new { tid = TenantId });

        return View(docs);
    }

    // GET /ImportExport/Import
    [HttpGet]
    public IActionResult Import() => View();

    // POST /ImportExport/Import
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(IFormFile file, string title, string? description = null)
    {
        if (file == null || file.Length == 0) return BadRequest("Arquivo inválido.");

        using var db = await OpenAsync();

        var docId = Guid.NewGuid();
        var code = $"DOC-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".bin";

        // cria documento
        await db.ExecuteAsync(@"
insert into ged.document
(id, tenant_id, code, title, description, status, visibility, created_at, created_by)
values
(@id, @tid, @code, @title, @desc, 'DRAFT'::ged.document_status_enum, 'INTERNAL'::ged.document_visibility_enum, now(), @uid);",
            new { id = docId, tid = TenantId, code, title, desc = description, uid = UserId });

        // versão
        var versionId = Guid.NewGuid();
        var verNo = 1;

        var uploadRoot = Path.Combine(_env.ContentRootPath, "App_Data", "uploads", TenantId.ToString(), docId.ToString(), $"v{verNo}");
        Directory.CreateDirectory(uploadRoot);

        var safeName = Path.GetFileName(file.FileName);
        var storagePath = Path.Combine(uploadRoot, safeName);

        await using (var fs = new FileStream(storagePath, FileMode.Create, FileAccess.Write))
        {
            await file.CopyToAsync(fs);
        }

        var sha256 = ComputeSha256(storagePath);

        await db.ExecuteAsync(@"
insert into ged.document_version
(id, tenant_id, document_id, version_number, file_name, file_extension, file_size_bytes, storage_path, checksum_sha256, content_type, created_at, created_by)
values
(@id, @tid, @docId, @ver, @fn, @ext, @sz, @path, @sha, @ct, now(), @uid);",
            new
            {
                id = versionId,
                tid = TenantId,
                docId,
                ver = verNo,
                fn = safeName,
                ext = ext.TrimStart('.'),
                sz = file.Length,
                path = storagePath,
                sha = sha256,
                ct = file.ContentType,
                uid = UserId
            });

        // aponta current_version_id
        await db.ExecuteAsync(@"
update ged.document
set current_version_id = @verId, updated_at = now(), updated_by=@uid
where tenant_id=@tid and id=@docId;",
            new { verId = versionId, tid = TenantId, docId, uid = UserId });

        return RedirectToAction(nameof(Index));
    }

    // GET /ImportExport/ExportCenter
    [HttpGet]
    public async Task<IActionResult> ExportCenter()
    {
        using var db = await OpenAsync();

        var docs = await db.QueryAsync(@"
select d.id, d.code, d.title, d.status, d.created_at, v.storage_path, v.file_name
from ged.document d
left join ged.document_version v on v.id = d.current_version_id
where d.tenant_id=@tid
order by d.created_at desc
limit 100;", new { tid = TenantId });

        return View(docs);
    }

    // GET /ImportExport/Export?docId=...
    [HttpGet]
    public async Task<IActionResult> Export(Guid docId)
    {
        using var db = await OpenAsync();

        var doc = await db.QueryFirstOrDefaultAsync(@"
select d.id, d.code, d.title, d.status, d.created_at, v.storage_path, v.file_name, v.content_type
from ged.document d
left join ged.document_version v on v.id = d.current_version_id
where d.tenant_id=@tid and d.id=@docId;", new { tid = TenantId, docId });

        if (doc == null) return NotFound("Documento não encontrado.");
        if (doc.storage_path == null) return BadRequest("Documento não possui versão atual com arquivo.");

        var meta = System.Text.Json.JsonSerializer.Serialize(new
        {
            doc.id,
            doc.code,
            doc.title,
            doc.status,
            doc.created_at,
            exported_at = DateTimeOffset.Now
        });

        var zipBytes = BuildZip(new Dictionary<string, byte[]>
        {
            ["metadata.json"] = Encoding.UTF8.GetBytes(meta),
            [$"files/{doc.file_name}"] = await System.IO.File.ReadAllBytesAsync((string)doc.storage_path)
        });

        return File(zipBytes, "application/zip", $"export-{doc.code}.zip");
    }

    private static string ComputeSha256(string filePath)
    {
        using var sha = SHA256.Create();
        using var fs = System.IO.File.OpenRead(filePath);
        var hash = sha.ComputeHash(fs);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static byte[] BuildZip(Dictionary<string, byte[]> files)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var kv in files)
            {
                var entry = zip.CreateEntry(kv.Key, CompressionLevel.Fastest);
                using var es = entry.Open();
                es.Write(kv.Value, 0, kv.Value.Length);
            }
        }
        return ms.ToArray();
    }
}