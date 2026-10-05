using System.Text.Json;

namespace InovaGed.Application.ArtificialIntelligence;

public sealed record MetadataFieldSuggestion(string Name, bool Sufficient, string? Text, bool? Flag, string? Evidence, string? Note);

public static class MetadataSuggestionReader
{
    public static IReadOnlyList<MetadataFieldSuggestion> Read(JsonElement fields, string sourceText)
    {
        var list = new List<MetadataFieldSuggestion>();
        foreach (var name in new[] { "title", "description", "isConfidential" })
        {
            if (!fields.TryGetProperty(name, out var field) || field.ValueKind != JsonValueKind.Object)
            {
                list.Add(new(name, false, null, null, null, "O modelo não informou este campo."));
                continue;
            }
            var sufficient = field.TryGetProperty("sufficient", out var flag) && flag.ValueKind == JsonValueKind.True;
            if (!sufficient)
            {
                list.Add(new(name, false, null, null, null, "Sem informação suficiente no texto processado."));
                continue;
            }
            var evidence = ReadString(field, "evidence");
            if (string.IsNullOrWhiteSpace(evidence) || !sourceText.Contains(evidence, StringComparison.OrdinalIgnoreCase))
            {
                list.Add(new(name, false, null, null, evidence, "A evidência não foi encontrada na versão processada."));
                continue;
            }
            if (name == "isConfidential")
            {
                if (!field.TryGetProperty("value", out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    list.Add(new(name, false, null, null, evidence, "Sem valor de sigilo sustentado."));
                    continue;
                }
                if (!value.GetBoolean())
                {
                    list.Add(new(name, false, null, null, evidence, "A ausência de dado sensível não comprova documento público. O sigilo atual permanece até decisão humana autorizada."));
                    continue;
                }
                list.Add(new(name, true, null, true, evidence, null));
                continue;
            }
            var text = ReadString(field, "value")?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                list.Add(new(name, false, null, null, evidence, "Sem valor sustentado por evidência."));
                continue;
            }
            list.Add(new(name, true, text, null, evidence, null));
        }
        return list;
    }

    private static string? ReadString(JsonElement field, string name) => field.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

public sealed record CatalogSuggestion(bool HasSuggestion, string? Name, string? Evidence, string? Justification, string? Note);

public static class CatalogSuggestionReader
{
    public static CatalogSuggestion Read(JsonElement root, string context, string nameProperty)
    {
        var note = root.TryGetProperty("confidence", out _) || root.TryGetProperty("modelIndicator", out _)
            ? "O indicador numérico declarado pelo modelo não é confiança calibrada e não autoriza a aplicação."
            : null;
        var outcome = root.TryGetProperty("outcome", out var outcomeElement) && outcomeElement.ValueKind == JsonValueKind.String ? outcomeElement.GetString() : null;
        if (string.Equals(outcome, "insufficient", StringComparison.OrdinalIgnoreCase))
            return new(false, null, null, null, Join(note, "O modelo informou que não há sugestão sustentada."));
        var name = root.TryGetProperty(nameProperty, out var nameElement) && nameElement.ValueKind == JsonValueKind.String ? nameElement.GetString()?.Trim() : null;
        if (string.IsNullOrWhiteSpace(name) || name.Equals("NENHUM", StringComparison.OrdinalIgnoreCase))
            return new(false, null, null, null, Join(note, "Nenhuma classe ou tipo foi sugerido."));
        var evidence = root.TryGetProperty("evidence", out var evidenceElement) && evidenceElement.ValueKind == JsonValueKind.String ? evidenceElement.GetString()?.Trim() : null;
        var justification = root.TryGetProperty("justification", out var justificationElement) && justificationElement.ValueKind == JsonValueKind.String ? justificationElement.GetString()?.Trim() : null;
        if (string.IsNullOrWhiteSpace(evidence) || !context.Contains(evidence, StringComparison.OrdinalIgnoreCase))
            return new(false, null, evidence, justification, Join(note, "A evidência não foi encontrada na versão processada. Nenhuma sugestão foi aceita."));
        return new(true, name, evidence, justification, note);
    }

    private static string? Join(string? left, string right) => string.IsNullOrWhiteSpace(left) ? right : $"{left} {right}";
}
