using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using InovaGed.Application.Labels.Canvas;

namespace InovaGed.Infrastructure.Labels;

/// <summary>Single compatibility boundary for RC22 layout-only and RC23+ envelope snapshots.</summary>
public static class LabelCanvasVersionSnapshotReader
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    public static LabelCanvasVersionSnapshotDto Read(string? snapshotJson, string designJson, int versionNo,
        string? storedHash, LabelCanvasDesignDto legacy)
    {
        JsonObject? root = null;
        try { root = JsonNode.Parse(snapshotJson ?? "") as JsonObject; } catch (JsonException) { }
        var envelope = root is not null && (root.ContainsKey("designJson") || root.ContainsKey("templateKey"));
        if (!envelope)
        {
            var layout = root is null ? designJson : root.ToJsonString();
            return FromLegacy(legacy, layout, versionNo, Sha256(layout), storedHash ?? Sha256(layout), "LAYOUT_LEGACY");
        }

        var fallbacks = root!["fallbacks"] as JsonObject;
        var layoutNode = root["designJson"];
        var layoutJson = layoutNode switch
        {
            JsonValue value when value.TryGetValue<string>(out var text) => text,
            not null => layoutNode.ToJsonString(),
            _ => designJson
        };
        string? S(string name, string? fallback = null) => root[name]?.GetValue<string?>() ?? fallback;
        Guid? G(string name, Guid? fallback = null) => Guid.TryParse(S(name), out var id) ? id : fallback;
        decimal D(string name, decimal fallback) => root[name]?.GetValue<decimal?>() ?? fallback;
        var versionHash = storedHash ?? ComputeVersionHash(root.ToJsonString(Json));
        return new LabelCanvasVersionSnapshotDto
        {
            TemplateKey=S("templateKey",legacy.TemplateKey)!, TemplateName=S("templateName",legacy.TemplateName)!,
            Description=S("description",legacy.Description), TemplateKind=S("templateKind",legacy.TemplateKind)!,
            SubjectType=S("subjectType",legacy.SubjectType)!, PaperKind=S("paperKind",legacy.PaperKind)!,
            WidthMm=D("widthMm",legacy.WidthMm), HeightMm=D("heightMm",legacy.HeightMm),
            Orientation=S("orientation",legacy.Orientation)!, DefaultBrandingProfileId=G("defaultBrandingProfileId",legacy.DefaultBrandingProfileId),
            BrandingBindingKey=S("brandingBindingKey",legacy.BrandingBindingKey),
            ClientNameFallback=fallbacks?["clientName"]?.GetValue<string?>() ?? S("clientNameFallback",legacy.ClientNameFallback),
            ContractNameFallback=fallbacks?["contractName"]?.GetValue<string?>() ?? S("contractNameFallback",legacy.ContractNameFallback),
            OrganizationNameFallback=fallbacks?["organizationName"]?.GetValue<string?>() ?? S("organizationNameFallback",legacy.OrganizationNameFallback),
            HeaderTitleFallback=fallbacks?["headerTitle"]?.GetValue<string?>() ?? S("headerTitleFallback",legacy.HeaderTitleFallback),
            HeaderSubtitleFallback=fallbacks?["headerSubtitle"]?.GetValue<string?>() ?? S("headerSubtitleFallback",legacy.HeaderSubtitleFallback),
            LabelContext=S("labelContext",legacy.LabelContext)!, DesignJson=layoutJson, VersionNo=versionNo,
            LayoutHash=S("layoutHash") ?? Sha256(layoutJson), VersionHash=versionHash,
            HashScope=S("hashScope","VERSION_ENVELOPE_V2")!
        };
    }

    public static string ComputeVersionHash(string envelopeJson)
    {
        var node = JsonNode.Parse(envelopeJson) ?? new JsonObject();
        if (node is JsonObject obj) { obj.Remove("versionHash"); obj.Remove("hashScope"); }
        return Sha256(node.ToJsonString(Json));
    }

    public static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static LabelCanvasVersionSnapshotDto FromLegacy(LabelCanvasDesignDto d,string json,int version,string layoutHash,string versionHash,string scope) => new()
    { TemplateKey=d.TemplateKey,TemplateName=d.TemplateName,Description=d.Description,TemplateKind=d.TemplateKind,SubjectType=d.SubjectType,
      PaperKind=d.PaperKind,WidthMm=d.WidthMm,HeightMm=d.HeightMm,Orientation=d.Orientation,DefaultBrandingProfileId=d.DefaultBrandingProfileId,
      BrandingBindingKey=d.BrandingBindingKey,ClientNameFallback=d.ClientNameFallback,ContractNameFallback=d.ContractNameFallback,
      OrganizationNameFallback=d.OrganizationNameFallback,HeaderTitleFallback=d.HeaderTitleFallback,HeaderSubtitleFallback=d.HeaderSubtitleFallback,
      LabelContext=d.LabelContext,DesignJson=json,VersionNo=version,LayoutHash=layoutHash,VersionHash=versionHash,HashScope=scope };
}
