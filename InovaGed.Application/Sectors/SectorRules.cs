namespace InovaGed.Application.Sectors;

public static class SectorPermissions
{
    public const string Consultar = "CONSULTAR";
    public const string Cadastrar = "CADASTRAR";
    public const string CorrigirVinculo = "CORRIGIR_VINCULO";
    public const string RevisarExtracao = "REVISAR_EXTRACAO";
    public const string Conferir = "CONFERIR";
    public const string Imprimir = "IMPRIMIR";
    public const string Compartilhar = "COMPARTILHAR";
    public const string Parametrizar = "PARAMETRIZAR";
    public const string Administrar = "ADMINISTRAR";

    public static readonly string[] All =
    [
        Consultar, Cadastrar, CorrigirVinculo, RevisarExtracao, Conferir, Imprimir, Compartilhar, Parametrizar, Administrar
    ];
}

public static class SectorReview
{
    public const string Recebido = "RECEBIDO";
    public const string Pendente = "PENDENTE";
    public const string NaoAplicavel = "NAO_APLICAVEL";
    public const string EmConferencia = "EM_CONFERENCIA";
    public const string Conferido = "CONFERIDO";
    public const string Inconsistente = "INCONSISTENTE";
    public const string Substituido = "SUBSTITUIDO";

    private static readonly Dictionary<string, string[]> Transitions = new(StringComparer.Ordinal)
    {
        [Pendente] = [Recebido, EmConferencia, NaoAplicavel],
        [Recebido] = [EmConferencia, Pendente, NaoAplicavel, Inconsistente, Substituido],
        [EmConferencia] = [Conferido, Inconsistente, Pendente],
        [Conferido] = [Inconsistente, Substituido],
        [Inconsistente] = [EmConferencia, Pendente],
        [NaoAplicavel] = [Pendente],
        [Substituido] = []
    };

    public static bool CanTransition(string? from, string to, string permission, string? justification, out string impediment)
    {
        impediment = "";
        var current = string.IsNullOrWhiteSpace(from) ? Pendente : from;
        if (!Transitions.TryGetValue(current, out var next) || !next.Contains(to, StringComparer.Ordinal))
        {
            impediment = "Esta situação de conferência não pode mudar para o destino informado.";
            return false;
        }

        var allowed = to switch
        {
            Recebido or Pendente => permission is SectorPermissions.Cadastrar or SectorPermissions.Conferir or SectorPermissions.Administrar,
            NaoAplicavel or EmConferencia or Conferido or Inconsistente => permission is SectorPermissions.Conferir or SectorPermissions.Administrar,
            Substituido => permission is SectorPermissions.CorrigirVinculo or SectorPermissions.Administrar,
            _ => false
        };
        if (!allowed)
        {
            impediment = "Seu acesso não permite esta conferência.";
            return false;
        }

        if (to == NaoAplicavel && string.IsNullOrWhiteSpace(justification))
        {
            impediment = "Marcar como não aplicável exige justificativa.";
            return false;
        }

        return true;
    }
}

public static class SectorApplicability
{
    public static bool IsRequired(string profileCode, bool profileEnabled, string? encounterKind, string applicability, SectorConditions conditions)
    {
        if (string.Equals(profileCode, "ANEXO_II", StringComparison.Ordinal))
            return profileEnabled;
        if (!string.Equals(profileCode, "ANEXO_I", StringComparison.Ordinal) || !string.Equals(encounterKind, "INTERNACAO", StringComparison.Ordinal))
            return false;
        return applicability switch
        {
            SectorCatalog.HospitalInternacao => true,
            SectorCatalog.Discharge => conditions.Discharged,
            SectorCatalog.HighCost => conditions.HighCost,
            SectorCatalog.Surgery => conditions.Surgery,
            SectorCatalog.Opme => conditions.Opme,
            SectorCatalog.WhenApplicable => false,
            _ => false
        };
    }
}

public sealed record SectorConditions(bool Surgery, bool Opme, bool HighCost, bool Discharged);

public static class SectorPatientCode
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var code = value.Trim();
        if (code.Length is 0 or > 80) return null;
        if (code.Any(ch => char.IsControl(ch) || ch is ' ' or '/' or '\\' or '?' or '#')) return null;
        return code;
    }
}

public static class SectorKindMap
{
    public static string? Resolve(string? rawCode, IReadOnlyDictionary<string, string> contract)
    {
        if (string.IsNullOrWhiteSpace(rawCode)) return null;
        if (!contract.TryGetValue(rawCode.Trim(), out var kind)) return null;
        return SectorCatalog.EncounterKinds.Contains(kind, StringComparer.Ordinal) ? kind : null;
    }
}

public static class SectorTextLocator
{
    public static int? PageAt(string? text, int matchIndex)
    {
        if (string.IsNullOrEmpty(text) || matchIndex < 0) return null;
        var window = text[..Math.Min(matchIndex, text.Length)];
        const string marker = "--- página ";
        int? page = null;
        var scan = 0;
        while (scan < window.Length)
        {
            var at = window.IndexOf(marker, scan, StringComparison.OrdinalIgnoreCase);
            if (at < 0) break;
            var end = window.IndexOf("---", at + marker.Length, StringComparison.Ordinal);
            if (end < 0) break;
            var number = window[(at + marker.Length)..end].Trim();
            page = int.TryParse(number, out var parsed) && parsed > 0 ? parsed : null;
            scan = end + 3;
        }

        return page;
    }
}

public static class SectorSearchTerms
{
    public static IReadOnlyList<string> Patterns(string term, IEnumerable<(string Name, string[] Synonyms)> catalog)
    {
        var clean = term.Trim().Replace("%", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal);
        var terms = new List<string> { clean };
        foreach (var row in catalog)
        {
            if (row.Synonyms is not { Length: > 0 } || row.Synonyms.All(item => !string.Equals(item, term.Trim(), StringComparison.OrdinalIgnoreCase)))
                continue;
            var name = row.Name.Trim().TrimEnd('.');
            if (name.Length >= 2) terms.Add(name);
        }

        return terms.Distinct(StringComparer.OrdinalIgnoreCase).Take(6)
            .Select(item => "%" + item + "%")
            .ToArray();
    }

    public static int FirstIndex(string text, IEnumerable<string> patterns)
    {
        var best = -1;
        foreach (var pattern in patterns)
        {
            var needle = pattern.Trim('%');
            if (needle.Length == 0) continue;
            var index = text.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && (best < 0 || index < best)) best = index;
        }

        return best;
    }
}

public static class SectorDuplicateNotice
{
    public static string Message(bool callerCanReadExisting, string? standardName)
        => callerCanReadExisting && !string.IsNullOrWhiteSpace(standardName)
            ? $"Já existe ocorrência deste conteúdo neste atendimento: {standardName}."
            : "Já existe um documento restrito com o mesmo conteúdo neste atendimento.";
}

public static class SectorExtraction
{
    public const string Prescrito = "PRESCRITO";
    public const string Planejado = "PLANEJADO";
    public const string MencionadoComoRealizado = "MENCIONADO_COMO_REALIZADO";
    public const string Sugerido = "SUGERIDO";

    public static IReadOnlyList<SectorFact> Read(string? text, string catalogCode)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var facts = new List<SectorFact>();
        var page = (int?)null;
        var lineNo = 0;
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("--- página ", StringComparison.OrdinalIgnoreCase) && line.EndsWith("---", StringComparison.Ordinal))
            {
                var number = line["--- página ".Length..^3].Trim();
                page = int.TryParse(number, out var parsed) && parsed > 0 ? parsed : null;
                continue;
            }

            if (LooksMedication(line))
                facts.Add(new(catalogCode == "DS23" || catalogCode == "DS24" ? Prescrito : Planejado, "medicamento", line, page, lineNo));
            else if (catalogCode is "DS08" && line.Contains("realiz", StringComparison.OrdinalIgnoreCase))
                facts.Add(new(MencionadoComoRealizado, "procedimento", line, page, lineNo));
            else if (catalogCode is "DS23" or "DS05" or "DS27" && line.Contains("proced", StringComparison.OrdinalIgnoreCase))
                facts.Add(new(Planejado, "procedimento", line, page, lineNo));
        }

        return facts;
    }

    private static bool LooksMedication(string line)
        => line.Contains(" mg", StringComparison.OrdinalIgnoreCase)
           || line.Contains(" ml", StringComparison.OrdinalIgnoreCase)
           || line.Contains("comprimido", StringComparison.OrdinalIgnoreCase)
           || line.Contains("via ", StringComparison.OrdinalIgnoreCase);
}

public sealed record SectorFact(string Context, string Category, string Excerpt, int? Page, int Line);

public static class SectorFindings
{
    public const string AccountUnavailable = "A conta e a autorização não estão integradas. A comparação é parcial. A leitura do prontuário não afirma que o item deixou de ser faturado.";

    public static string Describe(SectorFact fact)
        => fact.Context switch
        {
            SectorExtraction.MencionadoComoRealizado => "Procedimento mencionado como realizado na descrição. Isso localiza evidência e não define elegibilidade de faturamento.",
            SectorExtraction.Planejado => "Procedimento apenas planejado ou citado fora da descrição de realização. Não foi tratado como executado.",
            SectorExtraction.Prescrito => "Item prescrito. A prescrição não comprova administração nem procedimento executado.",
            _ => "Informação localizada para conferência humana."
        };
}

public static class SectorDeliveryRules
{
    public static IReadOnlyList<string> Blockers(IEnumerable<SectorDeliveryCandidate> items)
    {
        var blockers = new List<string>();
        foreach (var item in items)
        {
            if (item.Failed)
                blockers.Add($"{item.Name}: falha individual. O pacote não pode ser apresentado como completo.");
            if (item.PhysicalOriginal && !item.PhysicalEvidenceRecorded)
                blockers.Add($"{item.Name}: exige original físico. A impressão da cópia digital não registra a entrega.");
        }

        return blockers;
    }
}

public sealed record SectorDeliveryCandidate(string Name, bool PhysicalOriginal, bool PhysicalEvidenceRecorded, bool Failed);

public static class SectorFileRules
{
    public static readonly string[] OcrExtensions = [".pdf", ".jpg", ".jpeg", ".png", ".tif", ".tiff"];

    public static bool ContentMatches(string extension, ReadOnlySpan<byte> header)
    {
        extension = extension.ToLowerInvariant();
        if (header.Length < 4) return false;
        return extension switch
        {
            ".pdf" => header.Length >= 4 && header[0] == (byte)'%' && header[1] == (byte)'P' && header[2] == (byte)'D' && header[3] == (byte)'F',
            ".jpg" or ".jpeg" => header[0] == 0xFF && header[1] == 0xD8,
            ".png" => header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47,
            ".tif" or ".tiff" => (header[0] == 0x49 && header[1] == 0x49) || (header[0] == 0x4D && header[1] == 0x4D),
            ".docx" or ".xlsx" or ".pptx" => header[0] == 0x50 && header[1] == 0x4B,
            ".doc" or ".xls" or ".ppt" => header[0] == 0xD0 && header[1] == 0xCF,
            ".txt" => header.IndexOf((byte)0) < 0,
            _ => false
        };
    }
}

public static class ClinicalSourceMessages
{
    public const string Unavailable = "A integração de pacientes e atendimentos está indisponível. Nenhum dado foi simulado.";
    public const string NotFound = "Nenhum paciente foi localizado com esse código na origem informada.";
    public const string Failed = "A consulta à integração falhou. Nenhum atendimento local foi criado no lugar da origem.";
    public const string EncounterMissing = "O atendimento informado não está disponível para este paciente.";
}
