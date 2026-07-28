namespace InovaGed.Web.Services;

public interface IIconCatalog
{
    bool TryGetPath(string name, out string pathData);
}

public sealed class IconCatalog : IIconCatalog
{
    private static readonly IReadOnlyDictionary<string, string> Paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["dashboard"] = "M3 13h8V3H3v10Zm0 8h8v-6H3v6Zm10 0h8V11h-8v10Zm0-18v6h8V3h-8Z",
        ["document"] = "M6 2h9l5 5v15H6V2Zm8 2v5h5M9 13h8M9 17h8",
        ["document-search"] = "M5 2h10l4 4v8M14 18a4 4 0 1 1-8 0 4 4 0 0 1 8 0Zm-1 3 3 3",
        ["folder"] = "M2 6h8l2 2h10v13H2V6Z",
        ["upload"] = "M12 17V4m0 0L7 9m5-5 5 5M4 16v6h16v-6",
        ["ocr"] = "M4 3H2v5m18-5h2v5M4 21H2v-5m18 5h2v-5M7 8h10M7 12h10M7 16h6",
        ["shield-check"] = "M12 2 4 5v6c0 5 3 9 8 11 5-2 8-6 8-11V5l-8-3Zm-4 10 3 3 5-6",
        ["retention"] = "M4 5h16M7 3v4m10-4v4M5 9h14v12H5V9Zm4 4h6",
        ["loan"] = "M4 12h16m-4-4 4 4-4 4M8 8l-4 4 4 4",
        ["signature"] = "M4 18c4-1 3-8 6-8 3 0-1 8 2 8 2 0 3-4 5-4 1 0 0 4 3 4",
        ["audit"] = "M5 3h14v18H5V3Zm4 5h6M9 12h6M9 16h4",
        ["user"] = "M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8Zm-7 9c0-4 3-7 7-7s7 3 7 7",
        ["continuity"] = "M12 3a9 9 0 1 0 9 9M12 3v5l4-4M8 12a4 4 0 1 0 4-4",
        ["settings"] = "M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8Zm0-5v2m0 14v2M3 12h2m14 0h2M5.6 5.6 7 7m10 10 1.4 1.4M18.4 5.6 17 7M7 17l-1.4 1.4",
        ["logout"] = "M10 4H4v16h6M14 8l4 4-4 4m4-4H8"
    };

    public bool TryGetPath(string name, out string pathData) => Paths.TryGetValue(name, out pathData!);
}
