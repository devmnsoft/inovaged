namespace InovaGed.Web.Services;

public static class GedPanelResponseGate
{
    public static bool Applies(int responseGeneration, int currentGeneration, string responseDocumentId, string? openDocumentId, string? responseVersionId, string? openVersionId, bool panelOpen)
    {
        if (!panelOpen || responseGeneration != currentGeneration) return false;
        if (!string.Equals(responseDocumentId ?? "", openDocumentId ?? "", StringComparison.OrdinalIgnoreCase)) return false;
        return string.Equals(responseVersionId ?? "", openVersionId ?? "", StringComparison.OrdinalIgnoreCase);
    }
}
