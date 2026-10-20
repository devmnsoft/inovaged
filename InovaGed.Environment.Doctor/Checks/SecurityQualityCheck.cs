using System.Text.RegularExpressions;
using InovaGed.Environment.Doctor.Quality;

namespace InovaGed.Environment.Doctor.Checks;

public sealed partial class SecurityQualityCheck : IQualityCheck
{
    public string Name => "Security Scan";

    public async Task<IReadOnlyList<QualityFinding>> RunAsync(QualityContext context, CancellationToken cancellationToken)
    {
        var findings = new List<QualityFinding>();
        var root = Path.Combine(context.Root, "InovaGed.Web");
        if (!Directory.Exists(root))
            return [new(Name, QualityStatus.Pass, "Projeto web ausente neste recorte; antiforgery não se aplica.")];

        foreach (var file in Directory.EnumerateFiles(root, "*Controller.cs", SearchOption.AllDirectories))
        {
            var text = await File.ReadAllTextAsync(file, cancellationToken);
            var relative = Path.GetRelativePath(context.Root, file);
            if (!text.Contains("[Authorize", StringComparison.Ordinal) && !text.Contains("AllowAnonymous", StringComparison.Ordinal))
                findings.Add(new(Name, QualityStatus.Warning, $"Controller sem declaração explícita de autorização: {relative}", "Revisão de acesso necessária.", "Adicionar política ou registrar rota pública em allowlist.", Resource: relative));

            if (HasClassLevelAntiforgery(text))
                continue;

            foreach (Match post in HttpPostRegex().Matches(text))
            {
                if (ActionDeclaresAntiforgery(text, post))
                    continue;
                findings.Add(new(Name, QualityStatus.Fail, $"POST sem ValidateAntiForgeryToken: {relative}", "Risco de CSRF.", "Aplicar antiforgery na action ou registrar IgnoreAntiforgeryToken quando a proteção equivalente já existir.", Resource: relative));
            }
        }

        foreach (var file in Directory.EnumerateFiles(root, "appsettings*.json"))
        {
            var text = await File.ReadAllTextAsync(file, cancellationToken);
            if (Regex.IsMatch(text, "(?i)(password|pwd)\\s*=\\s*[^;]+") && !text.Contains("${", StringComparison.Ordinal))
                findings.Add(new(Name, QualityStatus.Fail, $"Possível segredo em connection string: {Path.GetRelativePath(context.Root, file)}", "Credencial pode ser exposta.", "Mover o segredo para variável de ambiente."));
        }

        return findings.Count == 0
            ? [new(Name, QualityStatus.Pass, "Antiforgery, autorização e segredos estáticos sem violações detectadas.")]
            : findings;
    }

    private static bool HasClassLevelAntiforgery(string text)
    {
        var classIndex = text.IndexOf("class ", StringComparison.Ordinal);
        if (classIndex < 0)
            return false;
        var head = text[..classIndex];
        return head.Contains("AutoValidateAntiforgeryToken", StringComparison.Ordinal)
            || head.Contains("IgnoreAntiforgeryToken", StringComparison.Ordinal);
    }

    private static bool ActionDeclaresAntiforgery(string text, Match post)
    {
        if (post.Value.Contains("ValidateAntiForgeryToken", StringComparison.Ordinal)
            || post.Value.Contains("IgnoreAntiforgeryToken", StringComparison.Ordinal))
            return true;

        var boundary = text.LastIndexOfAny([';', '}'], post.Index);
        var start = boundary < 0 ? Math.Max(0, post.Index - 800) : boundary + 1;
        var brace = FindMethodBodyStart(text, post.Index);
        var end = brace < 0 ? Math.Min(text.Length, post.Index + post.Length + 800) : brace;
        var window = text[start..end];
        return window.Contains("ValidateAntiForgeryToken", StringComparison.Ordinal)
            || window.Contains("IgnoreAntiforgeryToken", StringComparison.Ordinal)
            || window.Contains("AutoValidateAntiforgeryToken", StringComparison.Ordinal);
    }

    // Route templates such as [HttpPost("/x/{id}")] contain braces. Those braces are not the method body.
    private static int FindMethodBodyStart(string text, int from)
    {
        var inAttribute = false;
        var inString = false;
        for (var i = from; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\\') { i++; continue; }
                if (c == '"') inString = false;
                continue;
            }
            if (inAttribute)
            {
                if (c == '"') inString = true;
                else if (c == ']') inAttribute = false;
                continue;
            }
            if (c == '[') { inAttribute = true; continue; }
            if (c == '{') return i;
        }
        return -1;
    }

    [GeneratedRegex(@"\[HttpPost\b[^\]]*\]")]
    private static partial Regex HttpPostRegex();
}
