using System.Reflection;

namespace InovaGed.Web.Extensions;

public static class ResultExtensions
{
    /// <summary>
    /// Tenta extrair uma mensagem amigável de um Result/Result&lt;T&gt; sem depender do nome exato
    /// da propriedade (Message, ErrorMessage, Mensagem, Error, etc.).
    /// </summary>
    public static string? GetMensagem(this object? result)
    {
        if (result is null) return null;

        var t = result.GetType();

        // nomes comuns em Results
        var candidates = new[]
        {
            "Message", "Mensagem",
            "ErrorMessage", "Erro", "Error",
            "Description", "Detalhe"
        };

        foreach (var name in candidates)
        {
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (p is null) continue;

            var v = p.GetValue(result);
            var s = v?.ToString();
            if (!string.IsNullOrWhiteSpace(s))
                return s;
        }

        // fallback: se existir uma propriedade "Errors" ou "ErrorMessages" (coleção)
        var errorsProp = t.GetProperty("Errors", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                       ?? t.GetProperty("ErrorMessages", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (errorsProp is not null)
        {
            var v = errorsProp.GetValue(result);
            if (v is System.Collections.IEnumerable en)
            {
                foreach (var item in en)
                {
                    var s = item?.ToString();
                    if (!string.IsNullOrWhiteSpace(s))
                        return s;
                }
            }
        }

        return null;
    }
}
