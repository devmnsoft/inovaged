using System.Text.RegularExpressions;
using InovaGed.Environment.Doctor.Quality;

namespace InovaGed.Environment.Doctor.Checks;

public sealed class RazorQualityCheck : IQualityCheck
{
    public string Name => "Razor";

    public Task<IReadOnlyList<QualityFinding>> RunAsync(QualityContext context, CancellationToken cancellationToken)
    {
        var findings = new List<QualityFinding>();
        var views = Path.Combine(context.Root, "InovaGed.Web");
        if (!Directory.Exists(views))
            return Task.FromResult<IReadOnlyList<QualityFinding>>([new(Name, QualityStatus.Pass, "Views Razor não contêm padrões incompatíveis conhecidos.")]);

        foreach (var file in Directory.EnumerateFiles(views, "*.cshtml", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);
            var relative = Path.GetRelativePath(context.Root, file);
            foreach (var line in source.Replace("\r", "").Split('\n').Select((value, index) => (value, number: index + 1)))
            {
                if (ModelDirectiveHasExtraCode(line.value))
                    findings.Add(Bad(relative, line.number, "@model deve ficar sozinho na linha."));
                if (Regex.IsMatch(line.value, @"\?\?\s*\[\s*\]"))
                    findings.Add(Bad(relative, line.number, "uso de ?? [] não suportado em RuntimeCompilation; use Array.Empty<T>() ou new List<T>()."));
                if (Regex.IsMatch(line.value, @"ViewDataDictionary\s*\(\s*ViewData\s*\).*\{\s*\{\s*[""']Title[""']"))
                    findings.Add(Bad(relative, line.number, "Não usar Title em partial ViewData. Use AlertTitle, HeroTitle, CardTitle ou TableTitle."));
                if (Regex.IsMatch(line.value, @"@foreach\s*\([^\r\n]*new\s*\[\]"))
                    findings.Add(Bad(relative, line.number, "Crie lista tipada dentro de bloco @{ } antes do foreach."));
                if (Regex.IsMatch(line.value, @"<button\b(?=[^>]*(?:asp-action|class)=)(?![^>]*\btype\s*=)[^>]*>", RegexOptions.IgnoreCase))
                    findings.Add(Bad(relative, line.number, "Todo botão deve ter type explícito; use formaction/formmethod para actions diferentes."));
            }

            foreach (Match style in Regex.Matches(source, @"<style\b[^>]*>(.*?)</style>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                foreach (var token in new[] { "media", "page", "keyframes", "supports" })
                {
                    if (Regex.IsMatch(style.Groups[1].Value, $@"(?<!@)@{token}\b"))
                        findings.Add(Bad(relative, 1, $"@{token} dentro de <style> deve ser @@{token}; prefira CSS externo."));
                }
            }

            foreach (Match form in Regex.Matches(source, @"<form\b.*?</form>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                var inputs = Regex.Matches(form.Value, @"<(?:input|select|textarea)\b[^>]*asp-for\s*=\s*[""']([^""']+)[""'][^>]*>", RegexOptions.IgnoreCase);
                foreach (var duplicate in inputs.GroupBy(match => match.Groups[1].Value, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
                {
                    if (duplicate.All(IsRadioOrCheckbox))
                        continue;
                    findings.Add(Bad(relative, 1, $"Campo asp-for '{duplicate.Key}' duplicado no mesmo form; use um campo canônico."));
                }
            }
        }

        if (findings.Count == 0)
            findings.Add(new(Name, QualityStatus.Pass, "Views Razor não contêm padrões incompatíveis conhecidos."));
        return Task.FromResult<IReadOnlyList<QualityFinding>>(findings);
    }

    private static bool ModelDirectiveHasExtraCode(string line)
    {
        var match = Regex.Match(line.TrimStart('\uFEFF'), @"^\s*@model\b\s*(.*)$");
        if (!match.Success)
            return false;
        var rest = match.Groups[1].Value.Trim();
        if (rest.Length == 0 || rest.Contains(';') || rest.Contains('@'))
            return true;
        var index = 0;
        return !TryConsumeType(rest, ref index) || index < rest.Length;
    }

    private static bool TryConsumeType(string value, ref int index)
    {
        Skip(value, ref index);
        if (index >= value.Length)
            return false;
        if (value[index] is '(' or '<')
            return ConsumeBalanced(value, ref index);
        if (!char.IsLetter(value[index]) && value[index] != '_')
            return false;
        while (index < value.Length && (char.IsLetterOrDigit(value[index]) || value[index] is '_' or '.'))
            index++;
        Skip(value, ref index);
        if (index < value.Length && value[index] is '<' or '(' or '[')
        {
            if (!ConsumeBalanced(value, ref index))
                return false;
            Skip(value, ref index);
        }
        if (index < value.Length && value[index] == '?')
        {
            index++;
            Skip(value, ref index);
        }
        if (index < value.Length && value[index] == '[')
        {
            if (!ConsumeBalanced(value, ref index))
                return false;
            Skip(value, ref index);
        }
        return true;
    }

    private static bool ConsumeBalanced(string value, ref int index)
    {
        if (index >= value.Length || value[index] is not ('<' or '(' or '['))
            return false;
        var stack = new Stack<char>();
        stack.Push(value[index]);
        index++;
        while (index < value.Length && stack.Count > 0)
        {
            var current = value[index++];
            if (current is '<' or '(' or '[')
                stack.Push(current);
            else if (current is '>' or ')' or ']')
            {
                var open = stack.Pop();
                var matches = (open == '<' && current == '>') || (open == '(' && current == ')') || (open == '[' && current == ']');
                if (!matches)
                    return false;
            }
        }
        return stack.Count == 0;
    }

    private static void Skip(string value, ref int index)
    {
        while (index < value.Length && char.IsWhiteSpace(value[index]))
            index++;
    }

    private static bool IsRadioOrCheckbox(Match match) =>
        Regex.IsMatch(match.Value, @"\btype\s*=\s*[""'](?:radio|checkbox)[""']", RegexOptions.IgnoreCase);

    private QualityFinding Bad(string file, int line, string message) =>
        new(Name, QualityStatus.Fail, $"{file}:{line}: {message}", "A RuntimeCompilation pode falhar.", "Corrigir a sintaxe Razor antes do deploy.", Resource: file);
}
