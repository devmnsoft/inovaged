using System.Security.Cryptography;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Web.Models.Branding;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.Administracao)]
[Route("Administration/BrandAssets")]
public sealed class BrandAssetsController(IDbConnectionFactory dbFactory, IWebHostEnvironment environment, IConfiguration configuration) : GedControllerBase(dbFactory)
{
    private static readonly Dictionary<string, (string ContentType, byte[][] Signatures)> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = ("image/png", [new byte[] { 0x89,0x50,0x4e,0x47,0x0d,0x0a,0x1a,0x0a }]),
        [".jpg"] = ("image/jpeg", [new byte[] { 0xff,0xd8,0xff }]),
        [".jpeg"] = ("image/jpeg", [new byte[] { 0xff,0xd8,0xff }]),
        [".webp"] = ("image/webp", [System.Text.Encoding.ASCII.GetBytes("RIFF")])
    };

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        using var db = await OpenAsync();
        var rows = await db.QueryAsync<BrandAssetVm>(new CommandDefinition("select id,brand_name BrandName,asset_name AssetName,original_file_name OriginalFileName,content_type ContentType,file_extension FileExtension,file_size_bytes FileSizeBytes,storage_relative_path StorageRelativePath,width_px WidthPx,height_px HeightPx,is_default IsDefault,status,created_at CreatedAt,default_width_mm DefaultWidthMm,default_height_mm DefaultHeightMm,preserve_aspect_ratio PreserveAspectRatio,fit_mode FitMode,default_position DefaultPosition,alt_text AltText from ged.brand_asset where tenant_id=@tenant and reg_status='A' order by is_default desc,created_at desc", new { tenant = TenantId }, cancellationToken: ct));
        return View(rows.AsList());
    }

    [HttpGet("Create")]
    public IActionResult Create() => View(new BrandAssetUploadInput());

    [HttpPost("Create"), ValidateAntiForgeryToken, RequestFormLimits(MultipartBodyLengthLimit = 5_242_880)]
    public async Task<IActionResult> Create(BrandAssetUploadInput input, CancellationToken ct)
    {
        var file = input.File;
        var maxBytes = configuration.GetValue<long?>("Branding:MaxUploadBytes") ?? 5 * 1024 * 1024;
        var extension = Path.GetExtension(file?.FileName ?? "").ToLowerInvariant();
        if (extension == ".svg") ModelState.AddModelError(nameof(input.File), "Para segurança, envie a logo em PNG, JPG ou WEBP. O SVG só será permitido quando houver sanitização ativa.");
        else if (file is null || file.Length == 0) ModelState.AddModelError(nameof(input.File), "Selecione um arquivo de logo não vazio.");
        else if (file.Length > maxBytes) ModelState.AddModelError(nameof(input.File), $"A logo deve ter no máximo {maxBytes / 1024 / 1024} MB.");
        else if (!Allowed.TryGetValue(extension, out var format) || !string.Equals(file.ContentType, format.ContentType, StringComparison.OrdinalIgnoreCase)) ModelState.AddModelError(nameof(input.File), "Formato inválido. Envie PNG, JPG ou WEBP.");
        if (!ModelState.IsValid) return View(input);

        var expected = Allowed[extension];
        await using var source = file!.OpenReadStream();
        var header = new byte[12]; var read = await source.ReadAsync(header, ct); source.Position = 0;
        var signatureOk = expected.Signatures.Any(sig => read >= sig.Length && header.AsSpan(0, sig.Length).SequenceEqual(sig)) && (extension != ".webp" || read >= 12 && header.AsSpan(8, 4).SequenceEqual("WEBP"u8));
        if (!signatureOk) { ModelState.AddModelError(nameof(input.File), "O conteúdo do arquivo não corresponde ao formato informado."); return View(input); }

        var storedName = $"{Guid.NewGuid():N}{(extension == ".jpeg" ? ".jpg" : extension)}";
        var relative = Path.Combine("uploads", "branding", TenantId.ToString("N"), storedName).Replace('\\','/');
        var physical = Path.Combine(environment.WebRootPath, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(physical)!);
        await using (var output = System.IO.File.Create(physical)) await source.CopyToAsync(output, ct);
        string hash; await using (var hashStream = System.IO.File.OpenRead(physical)) hash = Convert.ToHexString(await SHA256.HashDataAsync(hashStream, ct)).ToLowerInvariant();
        var id = Guid.NewGuid();
        using var db = await OpenAsync();
        using var tx = db.BeginTransaction();
        if (input.IsDefault) await db.ExecuteAsync(new CommandDefinition("update ged.brand_asset set is_default=false where tenant_id=@tenant and reg_status='A'", new { tenant=TenantId }, tx, cancellationToken:ct));
        await db.ExecuteAsync(new CommandDefinition("insert into ged.brand_asset(id,tenant_id,brand_name,asset_name,original_file_name,stored_file_name,content_type,file_extension,file_size_bytes,file_hash_sha256,storage_relative_path,public_route,is_default,created_by,created_by_name) values(@id,@tenant,@brand,@asset,@original,@stored,@content,@extension,@size,@hash,@relative,@route,@isDefault,@userId,@userName)", new { id,tenant=TenantId,brand=input.BrandName.Trim(),asset=input.AssetName.Trim(),original=Path.GetFileName(file.FileName),stored=storedName,content=expected.ContentType,extension=Path.GetExtension(storedName),size=file.Length,hash,relative,route=$"/Administration/BrandAssets/{id}/File",isDefault=input.IsDefault,userId=UserId,userName=UserNameSafe },tx,cancellationToken:ct));
        tx.Commit(); TempData["Success"] = "Logo oficial cadastrada com segurança.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct) => await FindAsync(id, ct) is { } asset ? View(asset) : NotFound();

    [HttpGet("{id:guid}/Edit")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var asset = await FindAsync(id, ct);
        if (asset is null) return NotFound();
        return View(new BrandAssetEditInput { Id=asset.Id, BrandName=asset.BrandName, AssetName=asset.AssetName,
            DefaultWidthMm=asset.DefaultWidthMm, DefaultHeightMm=asset.DefaultHeightMm,
            PreserveAspectRatio=asset.PreserveAspectRatio, FitMode=asset.FitMode,
            DefaultPosition=asset.DefaultPosition, IsDefault=asset.IsDefault, AltText=asset.AltText });
    }

    [HttpPost("{id:guid}/Edit"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, BrandAssetEditInput input, CancellationToken ct)
    {
        if (id != input.Id) return BadRequest();
        if (!ModelState.IsValid) return View(input);
        using var db = await OpenAsync(); using var tx = db.BeginTransaction();
        if (input.IsDefault) await db.ExecuteAsync(new CommandDefinition("update ged.brand_asset set is_default=false where tenant_id=@tenant and reg_status='A'",new{tenant=TenantId},tx,cancellationToken:ct));
        var changed = await db.ExecuteAsync(new CommandDefinition("""
            update ged.brand_asset set brand_name=@BrandName,asset_name=@AssetName,default_width_mm=@DefaultWidthMm,
              default_height_mm=@DefaultHeightMm,preserve_aspect_ratio=@PreserveAspectRatio,fit_mode=@FitMode,
              default_position=@DefaultPosition,alt_text=@AltText,is_default=@IsDefault,updated_at=now()
            where id=@Id and tenant_id=@TenantId and status='ACTIVE' and reg_status='A'
            """,new{input.BrandName,input.AssetName,input.DefaultWidthMm,input.DefaultHeightMm,input.PreserveAspectRatio,input.FitMode,input.DefaultPosition,input.AltText,input.IsDefault,Id=id,TenantId},tx,cancellationToken:ct));
        if (changed == 0) return NotFound();
        tx.Commit(); TempData["Success"]="Configuração de impressão da logo atualizada.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:guid}/SetDefault"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SetDefault(Guid id, CancellationToken ct) { using var db=await OpenAsync(); using var tx=db.BeginTransaction(); var exists=await db.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from ged.brand_asset where id=@id and tenant_id=@tenant and reg_status='A' and status='ACTIVE')",new{id,tenant=TenantId},tx,cancellationToken:ct)); if(!exists)return NotFound(); await db.ExecuteAsync(new CommandDefinition("update ged.brand_asset set is_default=(id=@id) where tenant_id=@tenant and reg_status='A'",new{id,tenant=TenantId},tx,cancellationToken:ct)); tx.Commit(); return RedirectToAction(nameof(Index)); }

    [HttpPost("{id:guid}/Archive"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Archive(Guid id, string? reason, CancellationToken ct) { using var db=await OpenAsync(); var changed=await db.ExecuteAsync(new CommandDefinition("update ged.brand_asset set status='ARCHIVED',is_default=false,archived_at=now(),archived_by=@user,archive_reason=@reason where id=@id and tenant_id=@tenant and reg_status='A'",new{id,tenant=TenantId,user=UserId,reason},cancellationToken:ct)); return changed==0?NotFound():RedirectToAction(nameof(Index)); }

    [HttpGet("{id:guid}/Preview")]
    public IActionResult Preview(Guid id) => View(new PrintLogoViewModel { LogoUrl=Url.Action(nameof(File),new{id}),Alt="Preview da logo oficial" });

    [HttpGet("{id:guid}/File")]
    public async Task<IActionResult> File(Guid id, CancellationToken ct) { var asset=await FindAsync(id,ct); if(asset is null)return NotFound("Logo não encontrada."); var root=Path.GetFullPath(environment.WebRootPath); var path=Path.GetFullPath(Path.Combine(root,asset.StorageRelativePath.Replace('/',Path.DirectorySeparatorChar))); if(!path.StartsWith(root,StringComparison.Ordinal)||!System.IO.File.Exists(path))return NotFound("Logo não encontrada."); return PhysicalFile(path,asset.ContentType,enableRangeProcessing:true); }

    private async Task<BrandAssetVm?> FindAsync(Guid id,CancellationToken ct) { using var db=await OpenAsync(); return await db.QuerySingleOrDefaultAsync<BrandAssetVm>(new CommandDefinition("select id,brand_name BrandName,asset_name AssetName,original_file_name OriginalFileName,content_type ContentType,file_extension FileExtension,file_size_bytes FileSizeBytes,storage_relative_path StorageRelativePath,width_px WidthPx,height_px HeightPx,is_default IsDefault,status,created_at CreatedAt,default_width_mm DefaultWidthMm,default_height_mm DefaultHeightMm,preserve_aspect_ratio PreserveAspectRatio,fit_mode FitMode,default_position DefaultPosition,alt_text AltText from ged.brand_asset where id=@id and tenant_id=@tenant and reg_status='A'",new{id,tenant=TenantId},cancellationToken:ct)); }
}
