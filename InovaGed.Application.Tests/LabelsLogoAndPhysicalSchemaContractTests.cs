namespace InovaGed.Application.Tests;

public sealed class LabelsLogoAndPhysicalSchemaContractTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void Print_logo_partial_never_renders_an_empty_source()
    {
        var partial = Read("InovaGed.Web/Views/Shared/Branding/_PrintLogo.cshtml");
        Assert.Contains("ImageDataUriValidator.IsValidImageDataUri(source)", partial, StringComparison.Ordinal);
        Assert.Contains("Model?.PrintImageSource", partial, StringComparison.Ordinal);
        Assert.DoesNotContain("src=\"@Model.LogoUrl\"", partial, StringComparison.Ordinal);
    }

    [Fact]
    public void Print_logo_source_is_tenant_scoped_and_supports_data_uri()
    {
        var builder = Read("InovaGed.Web/Services/PrintLogoImageSourceBuilder.cs");
        Assert.Contains("tenant_id=@tenantId", builder, StringComparison.Ordinal);
        Assert.Contains("status='ACTIVE'", builder, StringComparison.Ordinal);
        Assert.Contains("data:{asset.ContentType};base64", builder, StringComparison.Ordinal);
        Assert.Contains("Path.IsPathRooted", builder, StringComparison.Ordinal);
    }

    [Fact]
    public void Physical_box_code_is_resolved_from_the_live_schema()
    {
        var service = Read("InovaGed.Infrastructure/PhysicalArchive2/PhysicalArchive2Service.cs");
        Assert.Contains("ResolvePhysicalBoxCodeExpressionAsync", service, StringComparison.Ordinal);
        Assert.Contains("array['box_code','box_no','code']", service, StringComparison.Ordinal);
        Assert.DoesNotContain("b.box_code", service, StringComparison.Ordinal);
        Assert.DoesNotContain("upper(box_code)", service, StringComparison.Ordinal);
    }

    [Fact]
    public void Physical_dashboard_aliases_do_not_escape_double_quotes_in_raw_sql()
    {
        var service = Read("InovaGed.Infrastructure/PhysicalArchive2/PhysicalArchive2Service.cs");
        Assert.DoesNotContain("as \\\"Boxes\\\"", service, StringComparison.Ordinal);
        Assert.Contains("as \"Boxes\"", service, StringComparison.Ordinal);
        Assert.Contains("as \"PendingChecks\"", service, StringComparison.Ordinal);
    }

    [Fact]
    public void Physical_archive_joins_are_tenant_scoped()
    {
        var service = Read("InovaGed.Infrastructure/PhysicalArchive2/PhysicalArchive2Service.cs");
        Assert.Contains("b.tenant_id=m.tenant_id and b.id=m.box_id", service, StringComparison.Ordinal);
        Assert.Contains("l.tenant_id=s.tenant_id and l.id=s.location_id", service, StringComparison.Ordinal);
        Assert.Contains("b.tenant_id=p.tenant_id and b.id=p.box_id", service, StringComparison.Ordinal);
        Assert.Contains("s.tenant_id=i.tenant_id and s.id=i.session_id", service, StringComparison.Ordinal);
    }

    [Fact]
    public void Upload_chunk_errors_keep_code_step_correlation_and_retry_contract()
    {
        var controller = Read("InovaGed.Web/Controller/UploadChunkController.cs");
        Assert.Contains("success = false, code, message, errorStep, correlationId, canRetry", controller, StringComparison.Ordinal);
        Assert.Contains("\"UPLOAD_CHUNK_SCHEMA_MISSING\" => StatusCodes.Status503ServiceUnavailable", controller, StringComparison.Ordinal);
        Assert.Contains("code is not \"UPLOAD_CHUNK_SCHEMA_MISSING\"", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void Upload_chunk_schema_hardening_is_registered()
    {
        var migration = Read("database/migrations/2026_10_07_ged_upload_chunk_schema_hardening.sql");
        var manifest = Read("database/migrations.manifest.json");
        var consolidated = Read("database/apply_all_required_migrations.sql");

        Assert.Contains("ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS batch_item_id", migration, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS metadata_json", migration, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE ged.upload_batch_item ADD COLUMN IF NOT EXISTS error_step", migration, StringComparison.Ordinal);
        Assert.Contains("2026_10_07_ged_upload_chunk_schema_hardening", manifest, StringComparison.Ordinal);
        Assert.Contains(@"\ir migrations/2026_10_07_ged_upload_chunk_schema_hardening.sql", consolidated, StringComparison.Ordinal);
    }

    [Fact]
    public void Wizard_posts_selected_asset_and_has_four_clear_stages()
    {
        var wizard = Read("InovaGed.Web/Views/Labels/PrintWizard.cshtml");
        Assert.Contains("asp-for=\"SelectedLogoAssetId\"", wizard, StringComparison.Ordinal);
        Assert.Contains("logoOrigin.value='SELECTED'", wizard, StringComparison.Ordinal);
        Assert.Contains("O que imprimir", wizard, StringComparison.Ordinal);
        Assert.Contains("Modelo da etiqueta", wizard, StringComparison.Ordinal);
        Assert.Contains("Logo e identidade visual", wizard, StringComparison.Ordinal);
        Assert.Contains("Conferência final", wizard, StringComparison.Ordinal);
    }

    private static string Read(string relativePath) => File.ReadAllText(Path.Combine(Root, relativePath));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "InovaGed.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Raiz do repositório não encontrada.");
    }
}
