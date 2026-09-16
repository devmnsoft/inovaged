using System.Text.RegularExpressions;
using InovaGed.Application.SmartSearch;

namespace InovaGed.Infrastructure.SmartSearch;

/// <summary>Deterministic refinement parser. It only changes the structured fields it explicitly recognizes.</summary>
public sealed partial class SmartSearchRefinementService : ISmartSearchRefinementService
{
    public SmartSearchRefinementResult Apply(SmartSearchQueryState current, string command)
    {
        ArgumentNullException.ThrowIfNull(current);
        var text = (command ?? string.Empty).Trim();
        var next = Copy(current);
        if (text.Length is < 2 or > 300) return Unsupported(next);

        var normalized = SmartSearchTextNormalizer.Normalize(text);
        if (normalized is "nova pesquisa" or "iniciar nova pesquisa")
            return Success("new-search", "Nova pesquisa iniciada; os filtros anteriores foram limpos.", new SmartSearchQueryState());

        if (normalized is "limpar filtros" or "remover filtros")
        {
            next.DocumentType = next.Unit = next.Classification = null; next.Year = null;
            return Success("remove-filter", "Todos os filtros foram removidos; os termos foram mantidos.", next);
        }

        if (normalized.Contains("retirar") || normalized.Contains("remover") || normalized.Contains("sem filtro"))
        {
            if (normalized.Contains("unidade")) { next.Unit = null; return Success("remove-filter", "Filtro de unidade removido.", next); }
            if (normalized.Contains("periodo") || normalized.Contains("ano")) { next.Year = null; return Success("remove-filter", "Filtro de período removido.", next); }
            if (normalized.Contains("tipo")) { next.DocumentType = null; return Success("remove-filter", "Filtro de tipo documental removido.", next); }
            if (normalized.Contains("classe") || normalized.Contains("classificacao")) { next.Classification = null; return Success("remove-filter", "Filtro de classe removido.", next); }
        }

        var year = YearRegex().Match(normalized);
        if (year.Success && (normalized.Contains("agora") || normalized.Contains("ano") || normalized.Contains("periodo") || normalized.Length <= 12))
        {
            var value = int.Parse(year.Value);
            var old = next.Year; next.Year = value;
            return Success(old.HasValue ? "replace-filter" : "add-filter", old.HasValue ? $"Período alterado de {old} para {value}." : $"Período {value} adicionado.", next);
        }

        var unit = UnitRegex().Match(text);
        if (unit.Success)
        {
            var value = CleanValue(unit.Groups[1].Value);
            if (string.IsNullOrWhiteSpace(value)) return Ambiguous(next, "Qual unidade deve ser aplicada?", "Somente da unidade financeira", "Somente da unidade administrativa");
            var old = next.Unit; next.Unit = value;
            return Success(old is null ? "add-filter" : "replace-filter", old is null ? $"Unidade “{value}” adicionada." : $"Unidade alterada de “{old}” para “{value}”.", next);
        }

        var phrase = PhraseRegex().Match(text);
        if (phrase.Success)
        {
            var value = CleanValue(phrase.Groups[1].Value).Trim('“', '”', '"');
            if (value.Length < 2) return Unsupported(next);
            next.Terms = string.IsNullOrWhiteSpace(next.Terms) ? $"\"{value}\"" : $"{next.Terms} \"{value}\"";
            return Success("append-term", $"Expressão “{value}” acrescentada aos termos.", next);
        }

        if (normalized.Contains("ordenar") || normalized.Contains("ordem") || normalized.StartsWith("mais "))
        {
            if (normalized.Contains("recente")) { next.Sort = "newest"; return Success("change-sort", "Ordenação alterada para mais recentes.", next); }
            if (normalized.Contains("antigo")) { next.Sort = "oldest"; return Success("change-sort", "Ordenação alterada para mais antigos.", next); }
            if (normalized.Contains("relev")) { next.Sort = "relevance"; return Success("change-sort", "Ordenação alterada para relevância.", next); }
            return Ambiguous(next, "Escolha a ordenação antes de aplicar.", "Ordenar por relevância", "Ordenar pelos mais recentes", "Ordenar pelos mais antigos");
        }

        if (normalized.StartsWith("buscar ") || normalized.StartsWith("pesquisar "))
        {
            next = new SmartSearchQueryState { Terms = text[(text.IndexOf(' ') + 1)..].Trim() };
            return Success("new-search", "Nova pesquisa iniciada; os filtros anteriores foram limpos.", next);
        }
        return Unsupported(next);
    }

    private static SmartSearchQueryState Copy(SmartSearchQueryState x) => new() { Terms = x.Terms, DocumentType = x.DocumentType, Unit = x.Unit, Classification = x.Classification, Year = x.Year, DateField = x.DateField, Sort = x.Sort };
    private static string CleanValue(string value) => value.Trim().TrimEnd('.', ';', ',');
    private static SmartSearchRefinementResult Success(string intent, string description, SmartSearchQueryState state) => new() { Supported = true, Intent = intent, Description = description, State = state };
    private static SmartSearchRefinementResult Unsupported(SmartSearchQueryState state) => new() { State = state, Description = "Comando não reconhecido. Use unidade, período, expressão, ordenação, limpar filtros ou nova pesquisa." };
    private static SmartSearchRefinementResult Ambiguous(SmartSearchQueryState state, string description, params string[] commands) => new() { Supported = true, Ambiguous = true, Intent = "ambiguous", Description = description, State = state, Options = commands.Select(x => new SmartSearchRefinementOption { Label = x, Command = x }).ToArray() };

    [GeneratedRegex(@"\b(?:19|20)\d{2}\b", RegexOptions.CultureInvariant)] private static partial Regex YearRegex();
    [GeneratedRegex(@"(?:somente|apenas)(?:\s+documentos)?\s+(?:da|de)\s+unidade\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex UnitRegex();
    [GeneratedRegex(@"(?:com|acrescentar|adicionar)\s+(?:a\s+)?(?:express[aã]o|termo)\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex PhraseRegex();
}
