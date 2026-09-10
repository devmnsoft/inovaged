namespace InovaGed.Application.Labels.Canvas;

public sealed record StarterElementLayout(
    string Key,
    string ElementType,
    string Label,
    decimal XMm,
    decimal YMm,
    decimal WidthMm,
    decimal HeightMm,
    decimal FontSizePt = 8.5m,
    string? BindingField = null,
    string? Text = null,
    bool Required = false);

public static class LabelCanvasStarterLayoutCalculator
{
    public enum AreaTier { Small, Medium, Large }

    public static AreaTier ResolveTier(decimal widthMm, decimal heightMm)
    {
        if (widthMm >= 90m && heightMm >= 60m) return AreaTier.Large;
        if (widthMm >= 65m && heightMm >= 40m) return AreaTier.Medium;
        return AreaTier.Small;
    }

    public static IReadOnlyList<StarterElementLayout> Calculate(
        decimal widthMm,
        decimal heightMm,
        string subjectType,
        LabelCanvasStarterKind starterKind,
        IReadOnlySet<string> availableFields,
        decimal safeMarginMm = 3m)
    {
        if (starterKind == LabelCanvasStarterKind.Blank) return Array.Empty<StarterElementLayout>();

        var tier = ResolveTier(widthMm, heightMm);
        var usableWidth = Math.Max(10m, widthMm - (2m * safeMarginMm));
        var usableHeight = Math.Max(10m, heightMm - (2m * safeMarginMm));
        var isManual = subjectType.Equals("ManualLabel", StringComparison.OrdinalIgnoreCase) ||
                       starterKind == LabelCanvasStarterKind.ManualLabel;

        var list = new List<StarterElementLayout>();

        if (isManual)
        {
            BuildManualLayout(list, tier, safeMarginMm, usableWidth, usableHeight, availableFields);
            return list;
        }

        switch (tier)
        {
            case AreaTier.Small:
                BuildSmallGenericLayout(list, starterKind, safeMarginMm, usableWidth, usableHeight, availableFields);
                break;
            case AreaTier.Medium:
                BuildMediumGenericLayout(list, starterKind, safeMarginMm, usableWidth, usableHeight, availableFields);
                break;
            default:
                BuildLargeGenericLayout(list, starterKind, safeMarginMm, usableWidth, usableHeight, availableFields);
                break;
        }

        return list;
    }

    private static void BuildManualLayout(
        List<StarterElementLayout> list,
        AreaTier tier,
        decimal m,
        decimal w,
        decimal h,
        IReadOnlySet<string> available)
    {
        if (tier == AreaTier.Small)
        {
            // Small: e.g. 50x30 (usable 44x24)
            var qrSize = Math.Min(16m, Math.Min(w * 0.40m, h - 2m));
            var leftW = Math.Max(10m, w - qrSize - 2m);

            AddFieldIfExists(list, "controlNumber", "field", "Nº de controle", m, m, leftW, 7m, 8m, true);
            AddFieldIfExists(list, "subject", "field", "Assunto", m, m + 7.5m, leftW, 6.5m, 7.5m, true);
            AddFieldIfExists(list, "qrPayload", "qr", "QR Code", m + leftW + 2m, m, qrSize, qrSize, 8m, false);
            AddFieldIfExists(list, "classification", "field", "Classificação", m, m + 14.5m, w, Math.Max(4m, h - 14.5m), 6.5m, false);
        }
        else if (tier == AreaTier.Medium)
        {
            // Medium: e.g. 70x40 (usable 64x34)
            var qrSize = Math.Min(20m, Math.Min(w * 0.35m, h * 0.60m));
            var leftW = Math.Max(10m, w - qrSize - 3m);

            AddFieldIfExists(list, "headerTitle", "field", "Título institucional", m, m, leftW, 5m, 8m, false);
            AddFieldIfExists(list, "controlNumber", "field", "Nº de controle", m, m + 5.5m, leftW, 7m, 9m, true);
            AddFieldIfExists(list, "subject", "field", "Assunto", m, m + 13m, leftW, 6m, 8m, true);
            AddFieldIfExists(list, "qrPayload", "qr", "QR Code", m + leftW + 3m, m, qrSize, qrSize, 8m, false);

            var bottomY = m + Math.Max(19.5m, qrSize + 1m);
            var remainingH = Math.Max(4m, (h - (bottomY - m)) / 2m);
            AddFieldIfExists(list, "classification", "field", "Classificação", m, bottomY, w, remainingH, 7.5m, false);
            AddFieldIfExists(list, "location", "field", "Localização", m, bottomY + remainingH + 0.5m, w, remainingH, 7m, false);
        }
        else
        {
            // Large: >= 90x60 (e.g. 100x70)
            var qrSize = 24m;
            var leftW = w - qrSize - 4m;

            AddFieldIfExists(list, "headerTitle", "field", "Título do cabeçalho", m, m, leftW, 6m, 9.5m, false);
            AddFieldIfExists(list, "clientName", "field", "Cliente", m, m + 7m, leftW, 6m, 8.5m, false);
            AddFieldIfExists(list, "controlNumber", "field", "Nº de controle", m, m + 14m, leftW, 9m, 11m, true);
            AddFieldIfExists(list, "subject", "field", "Assunto", m, m + 24m, leftW, 8m, 9m, true);
            AddFieldIfExists(list, "description", "field", "Descrição", m, m + 33m, leftW, 8m, 8.5m, false);
            AddFieldIfExists(list, "qrPayload", "qr", "QR Code", m + leftW + 4m, m, qrSize, qrSize, 8m, false);

            var bottomY = m + 42m;
            if (bottomY + 16m <= m + h)
            {
                AddFieldIfExists(list, "classification", "field", "Classificação", m, bottomY, w, 7m, 8m, false);
                AddFieldIfExists(list, "location", "field", "Localização", m, bottomY + 8m, w * 0.55m, 6.5m, 7.5m, false);
                AddFieldIfExists(list, "observations", "field", "Observações", m + w * 0.58m, bottomY + 8m, w * 0.42m, 6.5m, 7.5m, false);
            }
            else
            {
                AddFieldIfExists(list, "classification", "field", "Classificação", m, bottomY, w, Math.Max(4m, h - 42m), 7.5m, false);
            }
        }
    }

    private static void BuildSmallGenericLayout(
        List<StarterElementLayout> list,
        LabelCanvasStarterKind kind,
        decimal m,
        decimal w,
        decimal h,
        IReadOnlySet<string> available)
    {
        var hasQr = kind is LabelCanvasStarterKind.IdentificationQr or LabelCanvasStarterKind.Traceability or LabelCanvasStarterKind.Institutional;
        var qrSize = hasQr ? Math.Min(16m, Math.Min(w * 0.40m, h - 2m)) : 0m;
        var leftW = hasQr ? Math.Max(10m, w - qrSize - 2m) : w;

        var codeKey = FirstAvailable(available, "boxCode", "documentCode", "recordNumber", "processNumber", "controlNumber") ?? "controlNumber";
        var descKey = FirstAvailable(available, "documentTitle", "subject", "patientName", "boxNumber") ?? "subject";

        AddFieldIfExists(list, codeKey, "field", "Código", m, m, leftW, 7m, 8.5m, true);
        AddFieldIfExists(list, descKey, "field", "Descrição", m, m + 7.5m, leftW, 6.5m, 7.5m, false);

        if (hasQr)
        {
            AddFieldIfExists(list, "qrPayload", "qr", "QR Code", m + leftW + 2m, m, qrSize, qrSize, 8m, false);
        }

        if (kind is LabelCanvasStarterKind.Classification or LabelCanvasStarterKind.Traceability or LabelCanvasStarterKind.Institutional)
        {
            AddFieldIfExists(list, "classification", "field", "Classificação", m, m + 14.5m, w, Math.Max(4m, h - 14.5m), 6.5m, false);
        }
    }

    private static void BuildMediumGenericLayout(
        List<StarterElementLayout> list,
        LabelCanvasStarterKind kind,
        decimal m,
        decimal w,
        decimal h,
        IReadOnlySet<string> available)
    {
        var hasQr = kind is LabelCanvasStarterKind.IdentificationQr or LabelCanvasStarterKind.Traceability or LabelCanvasStarterKind.Institutional;
        var qrSize = hasQr ? Math.Min(20m, Math.Min(w * 0.35m, h * 0.55m)) : 0m;
        var leftW = hasQr ? Math.Max(10m, w - qrSize - 3m) : w;

        var codeKey = FirstAvailable(available, "boxCode", "documentCode", "recordNumber", "processNumber", "controlNumber") ?? "controlNumber";
        var descKey = FirstAvailable(available, "documentTitle", "subject", "patientName", "boxNumber") ?? "subject";

        AddFieldIfExists(list, "headerTitle", "field", "Título institucional", m, m, leftW, 5.5m, 8.5m, false);
        AddFieldIfExists(list, codeKey, "field", "Código", m, m + 6m, leftW, 7.5m, 9.5m, true);
        AddFieldIfExists(list, descKey, "field", "Descrição", m, m + 14m, leftW, 6.5m, 8m, false);

        if (hasQr)
        {
            AddFieldIfExists(list, "qrPayload", "qr", "QR Code", m + leftW + 3m, m, qrSize, qrSize, 8m, false);
        }

        var bottomY = m + Math.Max(21m, qrSize + 1m);
        var remainingH = Math.Max(4m, (h - (bottomY - m)) / 2m);

        if (kind is LabelCanvasStarterKind.Classification or LabelCanvasStarterKind.Traceability or LabelCanvasStarterKind.Institutional)
        {
            AddFieldIfExists(list, "classification", "field", "Classificação", m, bottomY, w, remainingH, 7.5m, false);
            AddFieldIfExists(list, "location", "field", "Localização", m, bottomY + remainingH + 0.5m, w, remainingH, 7m, false);
        }
    }

    private static void BuildLargeGenericLayout(
        List<StarterElementLayout> list,
        LabelCanvasStarterKind kind,
        decimal m,
        decimal w,
        decimal h,
        IReadOnlySet<string> available)
    {
        var hasQr = kind is LabelCanvasStarterKind.IdentificationQr or LabelCanvasStarterKind.Traceability or LabelCanvasStarterKind.Institutional;
        var qrSize = hasQr ? 24m : 0m;
        var leftW = hasQr ? w - qrSize - 4m : w;

        var codeKey = FirstAvailable(available, "boxCode", "documentCode", "recordNumber", "processNumber", "controlNumber") ?? "controlNumber";
        var descKey = FirstAvailable(available, "documentTitle", "subject", "patientName", "boxNumber") ?? "subject";

        if (kind is LabelCanvasStarterKind.Institutional or LabelCanvasStarterKind.Traceability)
        {
            AddFieldIfExists(list, "primaryLogo", "logo", "Logo", m, m, 26m, 12m, 8m, false);
            AddFieldIfExists(list, "headerTitle", "field", "Título institucional", m + 28m, m, leftW - 28m, 10m, 10m, false);
        }
        else
        {
            AddFieldIfExists(list, "headerTitle", "field", "Título institucional", m, m, leftW, 8m, 10m, false);
        }

        AddFieldIfExists(list, codeKey, "field", "Código", m, m + 13m, leftW, 9m, 11m, true);
        AddFieldIfExists(list, descKey, "field", "Descrição", m, m + 23m, leftW, 9m, 9m, false);

        if (hasQr)
        {
            AddFieldIfExists(list, "qrPayload", "qr", "QR Code", m + leftW + 4m, m, qrSize, qrSize, 8m, false);
        }

        if (kind is LabelCanvasStarterKind.Classification or LabelCanvasStarterKind.Traceability or LabelCanvasStarterKind.Institutional)
        {
            AddFieldIfExists(list, "classification", "field", "Classificação", m, m + 34m, w, 8m, 8.5m, false);
            AddFieldIfExists(list, "location", "field", "Localização", m, m + 43m, w, 8m, 8m, false);
        }
    }

    private static string? FirstAvailable(IReadOnlySet<string> available, params string[] candidates) =>
        candidates.FirstOrDefault(available.Contains);

    private static void AddFieldIfExists(
        List<StarterElementLayout> list,
        string key,
        string type,
        string label,
        decimal x,
        decimal y,
        decimal w,
        decimal h,
        decimal fontSizePt,
        bool required)
    {
        list.Add(new StarterElementLayout(key, type, label, Math.Round(x, 1), Math.Round(y, 1), Math.Round(w, 1), Math.Round(h, 1), fontSizePt, key, null, required));
    }
}
