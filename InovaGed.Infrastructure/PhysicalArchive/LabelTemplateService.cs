using InovaGed.Application.PhysicalArchive;
using System;
using System.Collections.Generic;

namespace InovaGed.Infrastructure.PhysicalArchive;

public sealed class LabelTemplateService : ILabelTemplateService
{
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
}
