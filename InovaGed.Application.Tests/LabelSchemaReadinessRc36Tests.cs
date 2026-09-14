using System.Text.Json;

namespace InovaGed.Application.Tests;

public sealed class LabelSchemaReadinessRc36Tests
{
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "InovaGed.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    [Fact] public void rc33_is_in_required_migration_catalog() => Assert.Contains("2026_09_11_rc33_collaboration_and_intake", Read("database","required_migrations.json"));
    [Fact] public void rc34_is_in_required_migration_catalog() => Assert.Contains("2026_09_14_rc34_operational_closure", Read("database","required_migrations.json"));
    [Fact] public void rc33_precedes_rc34()
    {
        var catalog=Read("database","required_migrations.json");
        Assert.True(catalog.IndexOf("2026_09_11_rc33",StringComparison.Ordinal)<catalog.IndexOf("2026_09_14_rc34",StringComparison.Ordinal));
    }
    [Fact] public void rc33_contains_lock_version_idempotent() => Assert.Contains("add column if not exists lock_version bigint not null default 1",Read("database","migrations","2026_09_11_rc33_collaboration_and_intake.sql"),StringComparison.OrdinalIgnoreCase);
    [Fact] public void label_schema_capabilities_detect_lock_version()
    {
        var source=Read("InovaGed.Infrastructure","Labels","LabelCanvasSchemaCapabilities.cs");
        Assert.Contains("information_schema.columns",source); Assert.Contains("to_regclass",source); Assert.Contains("TimeSpan.FromSeconds(45)",source);
    }
    [Fact] public void label_read_without_lock_version_uses_safe_projection()
    {
        var source=Read("InovaGed.Infrastructure","Labels","LabelCanvasDesignRepository.cs");
        Assert.Contains("1::bigint as LockVersion",source); Assert.Contains("capability.HasLockVersion",source);
    }
    [Fact] public void label_write_without_lock_version_is_blocked() => Assert.Contains("RequireWritableSchemaAsync",Read("InovaGed.Infrastructure","Labels","LabelCanvasDesignRepository.cs"));
    [Fact] public void quick_preview_schema_block_stops_retry_loop()
    {
        var source=Read("InovaGed.Web","wwwroot","js","labels-printwizard.js"); Assert.Contains("previewSchemaBlocked",source); Assert.Contains("LABEL_SCHEMA_UPDATE_REQUIRED",source);
    }
    [Fact] public void icon_legacy_names_have_no_missing_warning()
    {
        var source=Read("InovaGed.Web","Views","Labels","Shared","_LabelHelpDrawer.cshtml"); Assert.DoesNotContain("name=\"help-circle\"",source); Assert.DoesNotContain("name=\"x\"",source);
    }
    [Fact] public void last_problem_is_not_requested_twice_on_init()
    {
        Assert.Contains("window.__gedLastProblemChecked",Read("InovaGed.Web","wwwroot","js","ged-bulk-upload.js"));
        Assert.Contains("window.__gedLastProblemChecked",Read("InovaGed.Web","wwwroot","js","ged-bulk-actions.js"));
    }
}
