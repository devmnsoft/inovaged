namespace InovaGed.Application.Tests;

public sealed class ArchivalClassificationAndChunkUploadContractTests
{
    [Fact]
    public void Quick_classification_uses_archival_plan_and_retention_recalc()
    {
        var source = File.ReadAllText(Root("InovaGed.Web/Controller/GedClassificationsController.cs"));
        var quickList = Section(source, "public async Task<IActionResult> QuickList", "[HttpPost(\"/Ged/Documents/{id:guid}/Classification\")]");
        var update = Section(source, "public async Task<IActionResult> UpdateDocumentClassification", "private static async Task<string?> GetCurrentVersionOcrTextAsync");

        Assert.Contains("FROM ged.classification_plan c", quickList);
        Assert.Contains("classification_plan_version_item", quickList);
        Assert.DoesNotContain("ged.document_type", quickList, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[ValidateAntiForgeryToken]", source);
        Assert.DoesNotContain("[IgnoreAntiforgeryToken]", update);
        Assert.DoesNotContain("SaveManualAsync", update);
        Assert.Contains("classification_id = @ClassificationId", update);
        Assert.Contains("classification_version_id", update);
        Assert.Contains("ged.ai_retention_recalc_pending", update);
        Assert.Contains("QUICK_CLASSIFICATION_RECALC", update);
    }

    [Fact]
    public void Classification_plan_upsert_is_tenant_scoped_and_cycle_guarded()
    {
        var source = File.ReadAllText(Root("InovaGed.Infrastructure/ClassificationPlans/ClassificationPlanRepository.cs"));

        Assert.Contains("where ged.classification_plan.tenant_id = excluded.tenant_id", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("select tenant_id from ged.classification_plan where id=@id", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Classe pai não encontrada no tenant atual.", source);
        Assert.Contains("with recursive tree", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Document_classification_replace_operations_are_transactional_and_dispose_connections()
    {
        var source = File.ReadAllText(Root("InovaGed.Infrastructure/Classification/DocumentClassificationRepository.cs"));
        var replaceTags = Section(source, "public async Task ReplaceTagsAsync", "public async Task ReplaceMetadataAsync");
        var replaceMetadata = Section(source, "public async Task ReplaceMetadataAsync", "public async Task SetSuggestionAsync");

        Assert.DoesNotContain("        var conn = await _db.OpenAsync", source);
        Assert.Contains("await using var conn = await _db.OpenAsync", source);
        Assert.Contains("BeginTransactionAsync", replaceTags);
        Assert.Contains("BeginTransactionAsync", replaceMetadata);
        Assert.Contains("WHERE ged.document_tag.tenant_id = EXCLUDED.tenant_id", source);
        Assert.Contains("WHERE ged.document_metadata.tenant_id = EXCLUDED.tenant_id", source);
    }

    [Fact]
    public void Chunk_upload_preserves_metadata_and_verifies_parts_server_side()
    {
        var service = File.ReadAllText(Root("InovaGed.Infrastructure/Ged/Documents/UploadChunkService.cs"));
        var options = File.ReadAllText(Root("InovaGed.Application/Ged/Documents/DocumentUploadOptions.cs"));
        var policy = File.ReadAllText(Root("InovaGed.Application/Ged/Documents/DocumentUploadSizePolicy.cs"));

        Assert.Contains("public long? MaxFileSizeBytes", options);
        Assert.Contains("DocumentUploadSizePolicy.Exceeds", service);
        Assert.Contains("MaxFileSizeBytes", policy);
        Assert.Contains("MaxFileSizeMb", policy);
        Assert.Contains("request.Metadata.MarkAsIncomplete", service);
        Assert.Contains("MarkAsIncomplete = md.MarkAsIncomplete", service);
        Assert.Contains("using var sha = SHA256.Create()", service);
        Assert.Contains("Checksum da parte não confere", service);
        Assert.Contains(".attempt", service);
        Assert.Contains(".accepted.bak", service);
        Assert.Contains("Parte aceita com checksum incompatível", service);
        Assert.Contains("session.Status is \"COMPLETED\" or \"CANCELLED\" or \"ERROR\" or \"COMPLETING\"", service);
        Assert.Contains("TryMarkCompletingAsync", service);
        Assert.Contains("status='COMPLETING'", service);
        Assert.Contains("new FileInfo(assembled).Length != session.TotalSizeBytes", service);
    }

    [Fact]
    public void Ged_upload_catalog_and_completion_use_archival_classification_and_refresh_successful_documents()
    {
        var controller = File.ReadAllText(Root("InovaGed.Web/Controller/GedController.cs"));
        var batch = File.ReadAllText(Root("InovaGed.Infrastructure/Ged/Documents/UploadBatchService.cs"));
        var bulk = File.ReadAllText(Root("InovaGed.Infrastructure/Ged/Documents/DocumentBulkUploadService.cs"));
        var js = File.ReadAllText(Root("InovaGed.Web/wwwroot/js/ged-bulk-upload.js"));

        var options = Section(controller, "[HttpGet(\"/Ged/ClassificationOptions\")]", "[HttpGet(\"/Ged/DocumentsList\")]");
        Assert.Contains("FROM ged.classification_plan c", options);
        Assert.Contains("classification_plan_version_item", options);
        Assert.DoesNotContain("classification_node", options, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ClassificationId", batch);
        Assert.Contains("CLASSIFICATION_INVALID", batch);
        Assert.Contains("IDocumentBulkClassificationService", bulk);
        Assert.Contains("ClassificationId = null", bulk);
        Assert.Contains("getCurrentListingFolderId", js);
        Assert.DoesNotContain("if (hasBatchFailures()) { updateFooterActions(); return; }", js);
    }

    [Fact]
    public void Upload_size_policy_treats_zero_as_no_business_limit_and_bytes_as_authoritative()
    {
        var none = InovaGed.Application.Ged.Documents.DocumentUploadSizePolicy.Resolve(new InovaGed.Application.Ged.Documents.DocumentUploadOptions { MaxFileSizeBytes = 0, MaxFileSizeMb = 1 });
        Assert.False(none.HasBusinessLimit);
        Assert.Null(none.MaxBytes);

        var bytes = InovaGed.Application.Ged.Documents.DocumentUploadSizePolicy.Resolve(new InovaGed.Application.Ged.Documents.DocumentUploadOptions { MaxFileSizeBytes = 2_147_483_649, MaxFileSizeMb = 1 });
        Assert.Equal(2_147_483_649, bytes.MaxBytes);
        Assert.False(InovaGed.Application.Ged.Documents.DocumentUploadSizePolicy.Exceeds(new InovaGed.Application.Ged.Documents.DocumentUploadOptions { MaxFileSizeBytes = 2_147_483_649 }, 2_147_483_649, out _));
        Assert.True(InovaGed.Application.Ged.Documents.DocumentUploadSizePolicy.Exceeds(new InovaGed.Application.Ged.Documents.DocumentUploadOptions { MaxFileSizeBytes = 2_147_483_649 }, 2_147_483_650, out _));
    }

    private static string Root(string path) => GlobalJsonContractTests.Root(path);

    private static string Section(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start marker not found: {start}");
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"End marker not found: {end}");
        return source[startIndex..endIndex];
    }
}
