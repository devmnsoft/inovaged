namespace InovaGed.Application.Sectors;

public static class SectorEntryContract
{
    public const int ClientMax = 80;
    public const int EncounterMax = 80;
    public const int NameMax = 160;
    public const int CodeMax = 16;
    public const int IdempotencyMax = 80;

    public static readonly string[] Keys =
    [
        "integracao", "tipoIdentificador", "paciente", "codigoAtendimento", "nomeDocumento",
        "modo", "codigoDocumento", "documentoId", "versaoId"
    ];

    public static SectorEntryParse Parse(IReadOnlyDictionary<string, string?> query)
    {
        foreach (var key in query.Keys)
        {
            if (!Keys.Contains(key, StringComparer.OrdinalIgnoreCase))
                return SectorEntryParse.Invalid();
        }

        var client = Text(query, "integracao");
        var kindRaw = Text(query, "tipoIdentificador");
        var patient = Text(query, "paciente");
        var encounter = Text(query, "codigoAtendimento");
        var name = Text(query, "nomeDocumento");
        var code = Text(query, "codigoDocumento");
        var modeRaw = Text(query, "modo");
        if (client is null || patient is null || encounter is null || kindRaw is null)
            return SectorEntryParse.Invalid();
        if (client.Length > ClientMax || encounter.Length > EncounterMax)
            return SectorEntryParse.Invalid();
        if (name is not null && (name.Length > NameMax || name.Any(char.IsControl)))
            return SectorEntryParse.Invalid();
        if (code is not null && (code.Length > CodeMax || code.Any(char.IsControl)))
            return SectorEntryParse.Invalid();

        string kind;
        string identifier;
        if (string.Equals(kindRaw, "CPF", StringComparison.OrdinalIgnoreCase))
        {
            kind = "CPF";
            identifier = SectorCpf.Normalize(patient) ?? "";
            if (identifier.Length == 0)
                return SectorEntryParse.Invalid();
        }
        else if (string.Equals(kindRaw, "ID", StringComparison.OrdinalIgnoreCase))
        {
            kind = "ID";
            identifier = SectorPatientCode.Normalize(patient) ?? "";
            if (identifier.Length == 0)
                return SectorEntryParse.Invalid();
        }
        else
        {
            return SectorEntryParse.Invalid();
        }

        var mode = string.IsNullOrWhiteSpace(modeRaw) ? "consulta" : modeRaw.ToLowerInvariant();
        if (mode is not "consulta" and not "documento")
            return SectorEntryParse.Invalid();
        if (!GuidOrEmpty(query, "documentoId", out var documentId) || !GuidOrEmpty(query, "versaoId", out var versionId))
            return SectorEntryParse.Invalid();

        return new SectorEntryParse(true, new SectorEntryDraft(client, kind, identifier, encounter, name, code, documentId, versionId, mode));
    }

    public static bool IdempotencyKey(string? value, out string? key)
    {
        key = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (key is null) return true;
        if (key.Length > IdempotencyMax) return false;
        return key.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-');
    }

    public static bool IsContinuePath(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl)) return false;
        var raw = returnUrl.Trim();
        if (raw.Contains('?') || raw.Contains('#') || raw.Contains('\\') || raw.Contains("//", StringComparison.Ordinal))
            return false;
        const string prefix = "/DemaisSetores/Continuar/";
        if (!raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var rest = raw[prefix.Length..];
        return Guid.TryParse(rest, out var id) && rest.Equals(id.ToString("D"), StringComparison.OrdinalIgnoreCase);
    }

    private static string? Text(IReadOnlyDictionary<string, string?> query, string key)
    {
        var pair = query.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(pair.Value)) return null;
        var text = pair.Value.Trim();
        return text.Length == 0 ? null : text;
    }

    private static bool GuidOrEmpty(IReadOnlyDictionary<string, string?> query, string key, out Guid? id)
    {
        id = null;
        var text = Text(query, key);
        if (text is null) return true;
        if (!Guid.TryParse(text, out var parsed)) return false;
        id = parsed;
        return true;
    }
}

public sealed record SectorEntryParse(bool IsValid, SectorEntryDraft? Draft)
{
    public static SectorEntryParse Invalid() => new(false, null);
}

public sealed record SectorEntryDraft(
    string ClientId,
    string IdentifierKind,
    string Identifier,
    string EncounterCode,
    string? DocumentName,
    string? DocumentCode,
    Guid? DocumentId,
    Guid? VersionId,
    string Mode);

public sealed record SectorEntryIssue(bool Accepted, Guid? TicketId);

public sealed record SectorEntryContinuation(bool Redirect, string? PageUrl, int Status, string Message);
