using InovaGed.Application.PhysicalArchive;

namespace InovaGed.Infrastructure.PhysicalArchive;

public class LabelTemplateService : ILabelTemplateService
{
    private static readonly IReadOnlyDictionary<string, LabelTemplate> CurrentTemplates =
        new Dictionary<string, LabelTemplate>(StringComparer.OrdinalIgnoreCase)
        {
            ["BOX"] = new("LOCDESK_CAIXA", "1", "BOX"),
            ["DOCUMENT"] = new("LOCDESK_DOCUMENTO", "1", "DOCUMENT")
        };

    public LabelTemplate GetCurrent(string subjectType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectType);
        return CurrentTemplates.TryGetValue(subjectType, out var template)
            ? template
            : throw new InvalidOperationException($"Não existe template de impressão publicado para o tipo '{subjectType}'.");
    }
}
