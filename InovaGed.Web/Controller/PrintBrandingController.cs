using System.Data;
using Dapper;
using InovaGed.Application.Branding;
using InovaGed.Application.Common.Database;
using InovaGed.Web.Models.Branding;
using InovaGed.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InovaGed.Web.Controllers;

[Authorize(Policy = AppPolicies.Administracao)]
[Route("Administration/PrintBranding")]
public sealed class PrintBrandingController(
    IDbConnectionFactory factory,
    IPrintBrandingResolver resolver) : GedControllerBase(factory)
{
    private const string ViewRoot = "~/Views/Administration/PrintBranding/";

    private static readonly (string Context, string Key, string Description)[] InstitutionalCatalog =
    [
        (PrintBrandingContext.LabelTemplate, "LOCDESK_PASTA_V1", "Etiqueta LocDesk Pasta"),
        (PrintBrandingContext.LabelTemplate, "LOCDESK_CAIXA_V1", "Etiqueta LocDesk Caixa"),
        (PrintBrandingContext.LabelTemplate, "LOCDESK_PASTA_HOL_V1", "Etiqueta LocDesk HOL"),
        (PrintBrandingContext.LabelTemplate, "FACTORY_BOX_V1", "Etiqueta de caixa"),
        (PrintBrandingContext.LabelTemplate, "FACTORY_DOCUMENT_V1", "Etiqueta de documento"),
        (PrintBrandingContext.ContractMeasurement, "CONTRACT_MEASUREMENT_REPORT", "Relatório de medição"),
        (PrintBrandingContext.FiscalPortal, "FISCAL_ACCEPTANCE_TERM", "Termo do Portal do Fiscal"),
        (PrintBrandingContext.GovernanceReport, "GOVERNANCE_REPORT", "Relatório de governança"),
        (PrintBrandingContext.DocumentReport, "DOCUMENT_DISPATCH_SHEET", "Folha de despacho"),
        (PrintBrandingContext.DocumentCover, "DOCUMENT_COVER", "Documento de capa")
    ];

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        return BrandingView("Index", await Dashboard(ct));
    }

    [HttpGet("Profiles")]
    public async Task<IActionResult> Profiles(CancellationToken ct)
    {
        return BrandingView("Index", await Dashboard(ct));
    }

    [HttpGet("Profiles/Create")]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        await Assets(ct);
        return BrandingView("Create", new PrintBrandingProfileInput());
    }

    [HttpPost("Profiles/Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PrintBrandingProfileInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await Assets(ct);
            return BrandingView("Create", input);
        }

        if (!await LogosBelong(input, ct))
        {
            ModelState.AddModelError("", "Selecione somente logos ativas deste cliente.");
            await Assets(ct);
            return BrandingView("Create", input);
        }

        using var db = await OpenAsync();
        using var tx = db.BeginTransaction();

        if (input.IsDefault)
        {
            await db.ExecuteAsync(
                "update ged.print_branding_profile set is_default=false where tenant_id=@tenant and reg_status='A'",
                new { tenant = TenantId },
                tx);
        }

        var id = Guid.NewGuid();
        const string sql = """
            insert into ged.print_branding_profile(
                id, tenant_id, profile_code, profile_name, client_name, contract_name, organization_name,
                primary_logo_asset_id, secondary_logo_asset_id, header_title, header_subtitle,
                header_extra_line, footer_text, footer_extra_line, show_generated_at, show_page_number,
                show_protocol_info, logo_position, secondary_logo_position, primary_logo_width_mm,
                secondary_logo_width_mm, paper_size, orientation, margin_top_mm, margin_right_mm,
                margin_bottom_mm, margin_left_mm, is_default, created_by, created_by_name)
            values(
                @Id, @TenantId, @ProfileCode, @ProfileName, @ClientName, @ContractName, @OrganizationName,
                @PrimaryLogoAssetId, @SecondaryLogoAssetId, @HeaderTitle, @HeaderSubtitle,
                @HeaderExtraLine, @FooterText, @FooterExtraLine, @ShowGeneratedAt, @ShowPageNumber,
                @ShowProtocolInfo, @LogoPosition, @SecondaryLogoPosition, @PrimaryLogoWidthMm,
                @SecondaryLogoWidthMm, @PaperSize, @Orientation, @MarginTopMm, @MarginRightMm,
                @MarginBottomMm, @MarginLeftMm, @IsDefault, @UserId, @UserName)
            """;

        await db.ExecuteAsync(sql, new
        {
            Id = id,
            TenantId,
            input.ProfileCode,
            input.ProfileName,
            input.ClientName,
            input.ContractName,
            input.OrganizationName,
            input.PrimaryLogoAssetId,
            input.SecondaryLogoAssetId,
            input.HeaderTitle,
            input.HeaderSubtitle,
            input.HeaderExtraLine,
            input.FooterText,
            input.FooterExtraLine,
            input.ShowGeneratedAt,
            input.ShowPageNumber,
            input.ShowProtocolInfo,
            input.LogoPosition,
            input.SecondaryLogoPosition,
            input.PrimaryLogoWidthMm,
            input.SecondaryLogoWidthMm,
            input.PaperSize,
            input.Orientation,
            input.MarginTopMm,
            input.MarginRightMm,
            input.MarginBottomMm,
            input.MarginLeftMm,
            input.IsDefault,
            UserId,
            UserName = UserNameSafe
        }, tx);

        await Audit(db, tx, id, "PRINT_BRANDING_PROFILE_CREATED", "Perfil visual criado", ct);
        tx.Commit();
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("Profiles/{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        if (await Profile(id, ct) is null)
        {
            return NotFound();
        }

        var branding = await resolver.ResolveAsync(TenantId, "", "", id, null, ct);
        return BrandingView("Preview", new PrintBrandingPreviewVm { Branding = branding });
    }

    [HttpGet("Profiles/{id:guid}/Edit")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var profile = await Profile(id, ct);
        if (profile is null)
        {
            return NotFound();
        }

        await Assets(ct);
        return BrandingView("Edit", profile);
    }

    [HttpPost("Profiles/{id:guid}/Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, PrintBrandingProfileInput input, CancellationToken ct)
    {
        input.Id = id;
        if (!ModelState.IsValid || !await LogosBelong(input, ct))
        {
            if (ModelState.IsValid)
            {
                ModelState.AddModelError("", "Selecione somente logos ativas deste cliente.");
            }

            await Assets(ct);
            return BrandingView("Edit", input);
        }

        using var db = await OpenAsync();
        using var tx = db.BeginTransaction();
        const string sql = """
            update ged.print_branding_profile
            set profile_code=@ProfileCode,
                profile_name=@ProfileName,
                client_name=@ClientName,
                contract_name=@ContractName,
                organization_name=@OrganizationName,
                primary_logo_asset_id=@PrimaryLogoAssetId,
                secondary_logo_asset_id=@SecondaryLogoAssetId,
                header_title=@HeaderTitle,
                header_subtitle=@HeaderSubtitle,
                header_extra_line=@HeaderExtraLine,
                footer_text=@FooterText,
                footer_extra_line=@FooterExtraLine,
                show_generated_at=@ShowGeneratedAt,
                show_page_number=@ShowPageNumber,
                show_protocol_info=@ShowProtocolInfo,
                logo_position=@LogoPosition,
                secondary_logo_position=@SecondaryLogoPosition,
                primary_logo_width_mm=@PrimaryLogoWidthMm,
                secondary_logo_width_mm=@SecondaryLogoWidthMm,
                paper_size=@PaperSize,
                orientation=@Orientation,
                margin_top_mm=@MarginTopMm,
                margin_right_mm=@MarginRightMm,
                margin_bottom_mm=@MarginBottomMm,
                margin_left_mm=@MarginLeftMm,
                updated_at=now(),
                updated_by=@UserId,
                updated_by_name=@UserName
            where id=@id
              and tenant_id=@TenantId
              and reg_status='A'
            """;

        var changed = await db.ExecuteAsync(sql, new
        {
            input.ProfileCode,
            input.ProfileName,
            input.ClientName,
            input.ContractName,
            input.OrganizationName,
            input.PrimaryLogoAssetId,
            input.SecondaryLogoAssetId,
            input.HeaderTitle,
            input.HeaderSubtitle,
            input.HeaderExtraLine,
            input.FooterText,
            input.FooterExtraLine,
            input.ShowGeneratedAt,
            input.ShowPageNumber,
            input.ShowProtocolInfo,
            input.LogoPosition,
            input.SecondaryLogoPosition,
            input.PrimaryLogoWidthMm,
            input.SecondaryLogoWidthMm,
            input.PaperSize,
            input.Orientation,
            input.MarginTopMm,
            input.MarginRightMm,
            input.MarginBottomMm,
            input.MarginLeftMm,
            UserId,
            UserName = UserNameSafe,
            id,
            TenantId
        }, tx);

        if (changed == 0)
        {
            return NotFound();
        }

        await Audit(db, tx, id, "PRINT_BRANDING_PROFILE_UPDATED", "Perfil visual editado", ct);
        tx.Commit();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Profiles/{id:guid}/SetDefault")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetDefault(Guid id, CancellationToken ct)
    {
        using var db = await OpenAsync();
        using var tx = db.BeginTransaction();
        var exists = await db.ExecuteScalarAsync<bool>(
            "select exists(select 1 from ged.print_branding_profile where id=@id and tenant_id=@tenant and status='ACTIVE' and reg_status='A')",
            new { id, tenant = TenantId },
            tx);

        if (!exists)
        {
            return NotFound();
        }

        await db.ExecuteAsync(
            "update ged.print_branding_profile set is_default=(id=@id) where tenant_id=@tenant and reg_status='A'",
            new { id, tenant = TenantId },
            tx);
        await Audit(db, tx, id, "PRINT_BRANDING_PROFILE_SET_DEFAULT", "Perfil definido como padrão", ct);
        tx.Commit();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Profiles/{id:guid}/Duplicate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Duplicate(Guid id, CancellationToken ct)
    {
        using var db = await OpenAsync();
        using var tx = db.BeginTransaction();
        var copyId = Guid.NewGuid();
        var codeSuffix = copyId.ToString("N")[..8];
        const string sql = """
            insert into ged.print_branding_profile(
                id, tenant_id, profile_code, profile_name, client_name, contract_name,
                organization_name, primary_logo_asset_id, secondary_logo_asset_id, header_title,
                header_subtitle, header_extra_line, footer_text, footer_extra_line, show_generated_at,
                show_page_number, show_protocol_info, logo_position, secondary_logo_position,
                primary_logo_width_mm, secondary_logo_width_mm, preserve_logo_aspect_ratio,
                paper_size, orientation, margin_top_mm, margin_right_mm, margin_bottom_mm,
                margin_left_mm, status, is_default, created_by, created_by_name)
            select @copyId, tenant_id, left(profile_code, 71) || '-' || @codeSuffix,
                'Cópia de ' || profile_name, client_name, contract_name, organization_name,
                primary_logo_asset_id, secondary_logo_asset_id, header_title, header_subtitle,
                header_extra_line, footer_text, footer_extra_line, show_generated_at, show_page_number,
                show_protocol_info, logo_position, secondary_logo_position, primary_logo_width_mm,
                secondary_logo_width_mm, preserve_logo_aspect_ratio, paper_size, orientation,
                margin_top_mm, margin_right_mm, margin_bottom_mm, margin_left_mm, 'ACTIVE', false,
                @user, @userName
            from ged.print_branding_profile
            where id=@id
              and tenant_id=@tenant
              and status='ACTIVE'
              and reg_status='A'
            """;

        var copied = await db.ExecuteAsync(
            sql,
            new { copyId, codeSuffix, id, tenant = TenantId, user = UserId, userName = UserNameSafe },
            tx);
        if (copied == 0)
        {
            return NotFound();
        }

        await Audit(db, tx, copyId, "PRINT_BRANDING_PROFILE_DUPLICATED", "Perfil visual duplicado", ct);
        tx.Commit();
        return RedirectToAction(nameof(Edit), new { id = copyId });
    }

    [HttpPost("Profiles/{id:guid}/Archive")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Archive(Guid id, string? reason, CancellationToken ct)
    {
        using var db = await OpenAsync();
        using var tx = db.BeginTransaction();
        const string stateSql = """
            select p.is_default IsDefault,
                (select count(*)
                 from ged.print_branding_binding b
                 where b.tenant_id=p.tenant_id
                   and b.profile_id=p.id
                   and b.enabled
                   and b.reg_status='A') BindingCount
            from ged.print_branding_profile p
            where p.id=@id
              and p.tenant_id=@tenant
              and p.status='ACTIVE'
              and p.reg_status='A'
            """;
        var profile = await db.QuerySingleOrDefaultAsync<ArchiveState>(
            stateSql,
            new { id, tenant = TenantId },
            tx);

        if (profile is null)
        {
            return NotFound();
        }

        if (profile.IsDefault)
        {
            TempData["BrandingError"] = "Defina outro perfil como padrão antes de arquivar este.";
            return RedirectToAction(nameof(Index));
        }

        if (profile.BindingCount > 0 && string.IsNullOrWhiteSpace(reason))
        {
            TempData["BrandingError"] =
                $"Este perfil está vinculado a {profile.BindingCount} modelos. Confirme o arquivamento informando um motivo.";
            return RedirectToAction(nameof(Index));
        }

        const string archiveSql = """
            update ged.print_branding_profile
            set status='ARCHIVED',
                archived_at=now(),
                archived_by=@user,
                archive_reason=@reason
            where id=@id
              and tenant_id=@tenant
              and status='ACTIVE'
              and reg_status='A'
            """;
        var changed = await db.ExecuteAsync(
            archiveSql,
            new { id, tenant = TenantId, user = UserId, reason },
            tx);
        if (changed == 0)
        {
            return NotFound();
        }

        await Audit(db, tx, id, "PROFILE_ARCHIVED", "Perfil visual arquivado", ct);
        tx.Commit();
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("Profiles/{id:guid}/Preview")]
    public async Task<IActionResult> Preview(Guid id, CancellationToken ct)
    {
        var branding = await resolver.ResolveAsync(TenantId, "", "", id, null, ct);
        if (!branding.HasBranding)
        {
            return NotFound();
        }

        return BrandingView("Preview", new PrintBrandingPreviewVm { Branding = branding });
    }

    [HttpGet("Profiles/{id:guid}/Summary")]
    public async Task<IActionResult> Summary(Guid id, CancellationToken ct)
    {
        var profile = await Profile(id, ct);
        return profile is null
            ? NotFound()
            : Ok(new
            {
                profile.Id,
                profile.ProfileName,
                profile.ClientName,
                profile.ContractName,
                profile.OrganizationName,
                profile.PrimaryLogoName,
                logoUrl = profile.PrimaryLogoAssetId is Guid logo
                    ? $"/Administration/BrandAssets/{logo}/File"
                    : null
            });
    }

    [HttpGet("Bindings")]
    public async Task<IActionResult> Bindings(CancellationToken ct)
    {
        using var db = await OpenAsync();
        ViewBag.Profiles = await ProfilesList(db, ct);
        var catalog = await GetCatalogAsync(db, ct);
        const string sql = """
            select binding_context Context,
                binding_key BindingKey,
                profile_id ProfileId,
                p.profile_name ProfileName,
                case when p.primary_logo_asset_id is null then null
                     else '/Administration/BrandAssets/' || p.primary_logo_asset_id || '/File'
                end LogoUrl,
                b.enabled Enabled
            from ged.print_branding_binding b
            join ged.print_branding_profile p on p.id=b.profile_id
            where b.tenant_id=@tenant
              and b.reg_status='A'
            """;
        var rows = (await db.QueryAsync<PrintBrandingBindingVm>(
                new CommandDefinition(sql, new { tenant = TenantId }, cancellationToken: ct)))
            .ToDictionary(item => $"{item.Context}|{item.BindingKey}");

        var bindings = catalog.Select(item =>
        {
            var binding = rows.GetValueOrDefault($"{item.Context}|{item.Key}")
                ?? new PrintBrandingBindingVm { Context = item.Context, BindingKey = item.Key };
            binding.Description = item.Description;
            return binding;
        }).ToList();
        return BrandingView("Bindings", bindings);
    }

    [HttpPost("Bindings/Save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveBinding(PrintBrandingBindingInput input, CancellationToken ct)
    {
        using var db = await OpenAsync();
        var catalog = await GetCatalogAsync(db, ct);
        if (!catalog.Any(item => item.Context == input.Context && item.Key == input.BindingKey))
        {
            return BadRequest();
        }

        using var tx = db.BeginTransaction();
        if (input.ProfileId is null)
        {
            const string disableSql = """
                update ged.print_branding_binding
                set reg_status='I', updated_at=now()
                where tenant_id=@tenant
                  and binding_context=@context
                  and binding_key=@key
                  and reg_status='A'
                """;
            await db.ExecuteAsync(
                disableSql,
                new { tenant = TenantId, context = input.Context, key = input.BindingKey },
                tx);
            await Audit(db, tx, null, "BINDING_CHANGED", "Vínculo de identidade removido", ct);
            tx.Commit();
            return RedirectToAction(nameof(Bindings));
        }

        var validProfile = await db.ExecuteScalarAsync<bool>(
            "select exists(select 1 from ged.print_branding_profile where id=@id and tenant_id=@tenant and status='ACTIVE' and reg_status='A')",
            new { id = input.ProfileId, tenant = TenantId },
            tx);
        if (!validProfile)
        {
            return BadRequest();
        }

        const string upsertSql = """
            insert into ged.print_branding_binding(
                tenant_id, binding_context, binding_key, profile_id, enabled, created_by)
            values(@tenant, @context, @key, @profile, @enabled, @user)
            on conflict (tenant_id, binding_context, binding_key) where reg_status='A'
            do update set
                profile_id=excluded.profile_id,
                enabled=excluded.enabled,
                updated_at=now()
            """;
        await db.ExecuteAsync(upsertSql, new
        {
            tenant = TenantId,
            context = input.Context,
            key = input.BindingKey,
            profile = input.ProfileId,
            input.Enabled,
            user = UserId
        }, tx);
        await Audit(db, tx, input.ProfileId, "BINDING_CHANGED", "Vínculo de identidade alterado", ct);
        tx.Commit();
        return RedirectToAction(nameof(Bindings));
    }

    [HttpGet("TestPrint")]
    public async Task<IActionResult> TestPrint(Guid? profileId, CancellationToken ct)
    {
        using var db = await OpenAsync();
        ViewBag.Profiles = await ProfilesList(db, ct);
        var branding = await resolver.ResolveAsync(TenantId, "TEST", "TEST", profileId, null, ct);
        return BrandingView("TestPrint", new PrintBrandingPreviewVm { Branding = branding });
    }

    [HttpPost("TestPrint")]
    [ValidateAntiForgeryToken]
    public IActionResult TestPrintPost(Guid? profileId)
    {
        return RedirectToAction(nameof(TestPrint), new { profileId });
    }

    private ViewResult BrandingView(string viewName, object? model = null)
    {
        return base.View($"{ViewRoot}{viewName}.cshtml", model);
    }

    private async Task<PrintBrandingDashboardVm> Dashboard(CancellationToken ct)
    {
        using var db = await OpenAsync();
        var schemaReady = await HasTableAsync(db, "ged", "print_branding_profile")
            && await HasTableAsync(db, "ged", "brand_asset");
        if (!schemaReady)
        {
            return new PrintBrandingDashboardVm { SchemaReady = false };
        }

        var profiles = await ProfilesList(db, ct);
        var assets = await AssetsList(db, ct);
        var bindingCount = await db.ExecuteScalarAsync<int>(new CommandDefinition(
            "select count(*) from ged.print_branding_binding where tenant_id=@tenant and enabled and reg_status='A'",
            new { tenant = TenantId },
            cancellationToken: ct));
        return new PrintBrandingDashboardVm
        {
            SchemaReady = true,
            Profiles = profiles,
            Assets = assets,
            BindingCount = bindingCount
        };
    }

    private async Task<List<PrintBrandingProfileVm>> ProfilesList(IDbConnection db, CancellationToken ct)
    {
        const string sql = """
            select p.id,
                p.profile_code ProfileCode,
                p.profile_name ProfileName,
                p.client_name ClientName,
                p.contract_name ContractName,
                p.organization_name OrganizationName,
                p.primary_logo_asset_id PrimaryLogoAssetId,
                p.secondary_logo_asset_id SecondaryLogoAssetId,
                p.header_title HeaderTitle,
                p.header_subtitle HeaderSubtitle,
                p.header_extra_line HeaderExtraLine,
                p.footer_text FooterText,
                p.footer_extra_line FooterExtraLine,
                p.show_generated_at ShowGeneratedAt,
                p.show_page_number ShowPageNumber,
                p.show_protocol_info ShowProtocolInfo,
                p.logo_position LogoPosition,
                p.secondary_logo_position SecondaryLogoPosition,
                p.primary_logo_width_mm PrimaryLogoWidthMm,
                p.secondary_logo_width_mm SecondaryLogoWidthMm,
                p.paper_size PaperSize,
                p.orientation,
                p.margin_top_mm MarginTopMm,
                p.margin_right_mm MarginRightMm,
                p.margin_bottom_mm MarginBottomMm,
                p.margin_left_mm MarginLeftMm,
                p.is_default IsDefault,
                p.status,
                a.asset_name PrimaryLogoName,
                s.asset_name SecondaryLogoName
            from ged.print_branding_profile p
            left join ged.brand_asset a on a.id=p.primary_logo_asset_id
            left join ged.brand_asset s on s.id=p.secondary_logo_asset_id
            where p.tenant_id=@tenant
              and p.reg_status='A'
            order by p.is_default desc, p.profile_name
            """;
        return (await db.QueryAsync<PrintBrandingProfileVm>(new CommandDefinition(
            sql,
            new { tenant = TenantId },
            cancellationToken: ct))).AsList();
    }

    private async Task<PrintBrandingProfileVm?> Profile(Guid id, CancellationToken ct)
    {
        using var db = await OpenAsync();
        return (await ProfilesList(db, ct)).FirstOrDefault(profile => profile.Id == id);
    }

    private async Task Assets(CancellationToken ct)
    {
        using var db = await OpenAsync();
        ViewBag.Assets = await AssetsList(db, ct);
    }

    private async Task<List<BrandAssetVm>> AssetsList(IDbConnection db, CancellationToken ct)
    {
        const string sql = """
            select id,
                brand_name BrandName,
                asset_name AssetName,
                original_file_name OriginalFileName,
                content_type ContentType,
                file_extension FileExtension,
                file_size_bytes FileSizeBytes,
                is_default IsDefault,
                status,
                created_at CreatedAt
            from ged.brand_asset
            where tenant_id=@tenant
              and status='ACTIVE'
              and reg_status='A'
            order by is_default desc, asset_name
            """;
        return (await db.QueryAsync<BrandAssetVm>(new CommandDefinition(
            sql,
            new { tenant = TenantId },
            cancellationToken: ct))).AsList();
    }

    private async Task<bool> LogosBelong(PrintBrandingProfileInput input, CancellationToken ct)
    {
        var ids = new[] { input.PrimaryLogoAssetId, input.SecondaryLogoAssetId }
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        if (ids.Length == 0)
        {
            return true;
        }

        using var db = await OpenAsync();
        var count = await db.ExecuteScalarAsync<int>(new CommandDefinition(
            "select count(*) from ged.brand_asset where tenant_id=@tenant and id=any(@ids) and status='ACTIVE' and reg_status='A'",
            new { tenant = TenantId, ids },
            cancellationToken: ct));
        return count == ids.Length;
    }

    private async Task<IReadOnlyList<(string Context, string Key, string Description)>> GetCatalogAsync(
        IDbConnection db,
        CancellationToken ct)
    {
        var result = InstitutionalCatalog.ToList();
        if (!await HasTableAsync(db, "ged", "label_template_design"))
        {
            return result;
        }

        const string canvasSql = """
            select distinct on (d.template_key)
                'LABEL_TEMPLATE' as Context,
                d.template_key as Key,
                coalesce(v.snapshot_json->>'templateName', d.template_name)
                    || ' · Canvas '
                    || coalesce(v.snapshot_json->>'subjectType', d.subject_type) as Description
            from ged.label_template_design d
            join lateral (
                select snapshot_json
                from ged.label_template_design_version x
                where x.template_design_id = d.id
                  and x.status = 'PUBLISHED'
                  and x.reg_status in ('A', 'ACTIVE')
                order by x.version_no desc
                limit 1
            ) v on true
            where (d.tenant_id = @tenant or d.tenant_id is null)
              and d.reg_status in ('A', 'ACTIVE')
            order by d.template_key, d.tenant_id nulls last, d.updated_at desc nulls last
            """;
        var canvas = await db.QueryAsync<(string Context, string Key, string Description)>(
            new CommandDefinition(canvasSql, new { tenant = TenantId }, cancellationToken: ct));
        foreach (var item in canvas)
        {
            if (!result.Any(existing => existing.Context == item.Context && existing.Key == item.Key))
            {
                result.Add(item);
            }
        }

        return result;
    }

    private async Task Audit(
        IDbConnection db,
        IDbTransaction tx,
        Guid? profile,
        string type,
        string title,
        CancellationToken ct)
    {
        const string sql = """
            insert into ged.print_branding_audit_event(
                tenant_id, profile_id, event_type, title, performed_by, performed_by_name)
            values(@tenant, @profile, @type, @title, @user, @name)
            """;
        await db.ExecuteAsync(new CommandDefinition(
            sql,
            new { tenant = TenantId, profile, type, title, user = UserId, name = UserNameSafe },
            tx,
            cancellationToken: ct));
    }

    private sealed class ArchiveState
    {
        public bool IsDefault { get; init; }
        public int BindingCount { get; init; }
    }
}
