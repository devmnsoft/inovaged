using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace InovaGed.Application.ArtificialIntelligence;

/// <summary>
/// Stable identity of one human review. It binds tenant, execution, document, version and reviewer.
/// The fingerprint covers only the reviewer's decision, never the document state reconstructed after apply.
/// </summary>
public static class ReviewIdentity
{
    public static string OperationKey(Guid tenantId, Guid executionId, Guid documentId, Guid versionId, Guid reviewerId, string task)
    {
        var payload = string.Join('|', tenantId.ToString("N"), executionId.ToString("N"), documentId.ToString("N"), versionId.ToString("N"), reviewerId.ToString("N"), task ?? "");
        return Hash(payload);
    }

    public static string Fingerprint(string decisionJson) => Hash(decisionJson);

    public static string MetadataJson(bool titleSet, string? title, bool descriptionSet, string? description, bool secrecySet, bool? secrecy, string? justification) =>
        JsonSerializer.Serialize(new
        {
            kind = "metadata",
            title = new { selected = titleSet, value = titleSet ? title : null },
            description = new { selected = descriptionSet, value = descriptionSet ? description : null },
            isConfidential = new { selected = secrecySet, value = secrecySet ? secrecy : null, justification = Clean(justification) }
        });

    public static string CatalogJson(string task, Guid? selectedId) =>
        JsonSerializer.Serialize(new
        {
            kind = "catalog",
            task,
            selectedId = selectedId is null || selectedId == Guid.Empty ? (Guid?)null : selectedId
        });

    public static bool Equivalent(string? storedFingerprint, string? storedJson, string newFingerprint, string newJson)
    {
        if (!string.IsNullOrEmpty(storedFingerprint) && string.Equals(storedFingerprint, newFingerprint, StringComparison.OrdinalIgnoreCase)) return true;
        if (string.IsNullOrWhiteSpace(storedJson) || string.IsNullOrWhiteSpace(newJson)) return false;
        try
        {
            using var stored = JsonDocument.Parse(storedJson);
            using var incoming = JsonDocument.Parse(newJson);
            var left = stored.RootElement;
            var right = incoming.RootElement;
            var kind = Text(right, "kind");
            if (kind == "metadata") return MetadataEquivalent(left, right);
            if (kind == "catalog") return CatalogEquivalent(left, right);
            return false;
        }
        catch (JsonException) { return false; }
    }

    public static IReadOnlyList<ReviewFieldDecision> Fields(string? decisionJson, bool suggestionAvailable, JsonElement? suggestion)
    {
        if (string.IsNullOrWhiteSpace(decisionJson)) return [];
        try
        {
            using var document = JsonDocument.Parse(decisionJson);
            var root = document.RootElement;
            var kind = Text(root, "kind");
            if (kind == "metadata" || root.TryGetProperty("title", out _))
                return MetadataFields(root, suggestionAvailable, suggestion);
            if (kind == "catalog" || root.TryGetProperty("selectedId", out _))
                return [CatalogField(root)];
            return [];
        }
        catch (JsonException) { return []; }
    }

    public static string Situation(string kind, string? outcome, bool pendingOpen, bool pendingResolved, int attempts, bool aiExpired)
    {
        if (string.Equals(kind, "suggestion", StringComparison.Ordinal))
            return aiExpired ? "Resultado da IA expirado" : "Sugestão gerada";
        if (pendingOpen) return "Aplicação com recálculo pendente";
        if (pendingResolved && attempts > 1) return "Pendência recuperada";
        if (string.Equals(outcome, "Recorded", StringComparison.OrdinalIgnoreCase)) return "Revisão registrada sem alteração";
        return "Aplicação concluída";
    }

    private static bool MetadataEquivalent(JsonElement stored, JsonElement incoming)
    {
        if (Text(stored, "kind") == "metadata")
            return FieldEquals(stored, incoming, "title") && FieldEquals(stored, incoming, "description") && SecrecyEquals(stored, incoming);
        return LegacyFieldEquals(stored, incoming, "title") && LegacyFieldEquals(stored, incoming, "description") && LegacySecrecyEquals(stored, incoming);
    }

    private static bool CatalogEquivalent(JsonElement stored, JsonElement incoming)
    {
        var incomingId = GuidText(incoming, "selectedId");
        if (Text(stored, "kind") == "catalog")
            return string.Equals(Text(stored, "task"), Text(incoming, "task"), StringComparison.Ordinal) && string.Equals(GuidText(stored, "selectedId"), incomingId, StringComparison.OrdinalIgnoreCase);
        return string.Equals(GuidText(stored, "selectedId"), incomingId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool FieldEquals(JsonElement stored, JsonElement incoming, string name) =>
        stored.TryGetProperty(name, out var left) && incoming.TryGetProperty(name, out var right)
        && Bool(left, "selected") == Bool(right, "selected")
        && string.Equals(Text(left, "value") ?? "", Text(right, "value") ?? "", StringComparison.Ordinal);

    private static bool SecrecyEquals(JsonElement stored, JsonElement incoming)
    {
        if (!stored.TryGetProperty("isConfidential", out var left) || !incoming.TryGetProperty("isConfidential", out var right)) return false;
        return Bool(left, "selected") == Bool(right, "selected")
            && NullableBool(left, "value") == NullableBool(right, "value")
            && string.Equals(Clean(Text(left, "justification")) ?? "", Clean(Text(right, "justification")) ?? "", StringComparison.Ordinal);
    }

    private static bool LegacyFieldEquals(JsonElement stored, JsonElement incoming, string name)
    {
        if (!stored.TryGetProperty(name, out var left) || !incoming.TryGetProperty(name, out var right)) return false;
        var selected = !Bool(left, "rejected");
        if (selected != Bool(right, "selected")) return false;
        if (!selected) return true;
        return string.Equals(Text(left, "applied") ?? "", Text(right, "value") ?? "", StringComparison.Ordinal);
    }

    private static bool LegacySecrecyEquals(JsonElement stored, JsonElement incoming)
    {
        if (!stored.TryGetProperty("isConfidential", out var left) || !incoming.TryGetProperty("isConfidential", out var right)) return false;
        var selected = !Bool(left, "rejected");
        if (selected != Bool(right, "selected")) return false;
        if (selected && NullableBool(left, "applied") != NullableBool(right, "value")) return false;
        return string.Equals(Clean(Text(left, "justification")) ?? "", Clean(Text(right, "justification")) ?? "", StringComparison.Ordinal);
    }

    private static List<ReviewFieldDecision> MetadataFields(JsonElement root, bool suggestionAvailable, JsonElement? suggestion)
    {
        var list = new List<ReviewFieldDecision>(3);
        list.Add(One(root, "title", "Título", suggestionAvailable, suggestion, false));
        list.Add(One(root, "description", "Descrição", suggestionAvailable, suggestion, false));
        list.Add(One(root, "isConfidential", "Sigilo", suggestionAvailable, suggestion, true));
        return list;
    }

    private static ReviewFieldDecision One(JsonElement root, string name, string label, bool suggestionAvailable, JsonElement? suggestion, bool flag)
    {
        if (!root.TryGetProperty(name, out var field)) return new(name, label, "rejeitado", null);
        if (Text(root, "kind") == "metadata" || field.TryGetProperty("selected", out _))
        {
            var selected = Bool(field, "selected");
            var value = flag ? FlagText(NullableBool(field, "value")) : Text(field, "value");
            return new(name, label, Effect(selected, value, suggestionAvailable, Suggested(suggestion, name, flag)), selected ? value : null);
        }
        var legacySelected = !Bool(field, "rejected");
        var legacyValue = flag ? FlagText(NullableBool(field, "applied")) : Text(field, "applied");
        var corrected = Text(field, "corrected");
        var effect = !legacySelected ? "rejeitado" : !string.IsNullOrEmpty(corrected) ? "corrigido" : "aplicado";
        return new(name, label, effect, legacySelected ? legacyValue : null);
    }

    private static ReviewFieldDecision CatalogField(JsonElement root)
    {
        var id = GuidText(root, "selectedId");
        var name = Text(root, "selectedName");
        if (string.IsNullOrEmpty(id)) return new("classification", "Item", "rejeitado", null);
        return new("classification", "Item", "aplicado", string.IsNullOrEmpty(name) ? id : name);
    }

    private static string Effect(bool selected, string? value, bool suggestionAvailable, string? suggested)
    {
        if (!selected) return "rejeitado";
        if (!suggestionAvailable) return "aplicado";
        return string.Equals(value ?? "", suggested ?? "", StringComparison.Ordinal) ? "aplicado" : "corrigido";
    }

    private static string? Suggested(JsonElement? suggestion, string name, bool flag)
    {
        if (suggestion is not { } root || !root.TryGetProperty("fields", out var fields)) return null;
        if (!fields.TryGetProperty(name, out var field) || field.ValueKind != JsonValueKind.Object) return null;
        if (!field.TryGetProperty("sufficient", out var sufficient) || sufficient.ValueKind != JsonValueKind.True) return null;
        if (!field.TryGetProperty("value", out var value)) return null;
        if (flag) return value.ValueKind == JsonValueKind.True ? "Sigiloso" : null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static string Hash(string payload) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? Text(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool Bool(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    private static bool? NullableBool(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.True) return true;
        if (value.ValueKind == JsonValueKind.False) return false;
        return null;
    }
    private static string? GuidText(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return "";
        if (value.ValueKind != JsonValueKind.String) return null;
        var text = value.GetString();
        return Guid.TryParse(text, out var parsed) && parsed != Guid.Empty ? parsed.ToString("D") : "";
    }
    private static string? FlagText(bool? value) => value is null ? null : value.Value ? "Sigiloso" : "Não sigiloso";
}

public sealed record ReviewFieldDecision(string Name, string Label, string Effect, string? Value);
