using InovaGed.Application.PhysicalArchive;
using System;
using System.Collections.Generic;

namespace InovaGed.Infrastructure.PhysicalArchive;

public sealed class LabelTemplateService : ILabelTemplateService
{
<<<<<<< HEAD
    private static readonly IReadOnlyDictionary<string, LabelTemplate> Templates =
        new Dictionary<string, LabelTemplate>(StringComparer.OrdinalIgnoreCase)
        {
            ["BOX"] = new("BOX_ATLAS", "2", "BOX"),
            ["DOCUMENT"] = new("DOCUMENT_ATLAS", "2", "DOCUMENT"),
            ["BATCH"] = new("BATCH_ATLAS", "2", "BATCH")
        };

    public LabelTemplate GetCurrent(string subjectType) =>
        Templates.TryGetValue(subjectType, out var template)
            ? template
            : throw new ArgumentOutOfRangeException(nameof(subjectType), "Tipo de etiqueta não suportado.");
=======
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
>>>>>>> 8bdbe44e2116af826540e6b80059583e26a043ab
}
