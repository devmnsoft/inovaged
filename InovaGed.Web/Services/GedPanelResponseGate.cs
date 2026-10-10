namespace InovaGed.Web.Services;

public static class GedPanelResponseGate
{
    public static bool Applies(int responseGeneration, int currentGeneration, string responseDocumentId, string? openDocumentId, string? responseVersionId, string? openVersionId, bool panelOpen)
    {
        if (!panelOpen || responseGeneration != currentGeneration) return false;
        if (!string.Equals(responseDocumentId ?? "", openDocumentId ?? "", StringComparison.OrdinalIgnoreCase)) return false;
        var responseVersion = responseVersionId ?? "";
        var openVersion = openVersionId ?? "";
        if (responseVersion.Length == 0 || openVersion.Length == 0) return true;
        return string.Equals(responseVersion, openVersion, StringComparison.OrdinalIgnoreCase);
    }
}
