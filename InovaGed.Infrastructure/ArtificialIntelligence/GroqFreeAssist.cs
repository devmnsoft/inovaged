using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using InovaGed.Application.ArtificialIntelligence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InovaGed.Infrastructure.ArtificialIntelligence;

public sealed class GroqFreeAssist : IFreeAssist
{
    private readonly HttpClient _http;
    private readonly FreeAssistOptions _options;
    private readonly ILogger<GroqFreeAssist> _logger;

    public GroqFreeAssist(HttpClient http, IOptions<FreeAssistOptions> options, ILogger<GroqFreeAssist> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public bool CanAssistSearch => Ready(_options.AssistSearch);
    public bool CanAssistOcr => Ready(_options.AssistOcr);

    public async Task<IReadOnlyList<string>> ExpandSearchTermsAsync(string query, CancellationToken ct)
    {
        if (!CanAssistSearch || string.IsNullOrWhiteSpace(query)) return [];
        var prompt = "A consulta abaixo foi digitada por um usuário. Devolva somente um JSON array com até 6 sinônimos ou ampliações curtas em português que ajudem a achar o mesmo documento. Não invente prontuário, protocolo, datas ou nomes. Não explique. Consulta: " + query.Trim();
        var text = await CompleteAsync(prompt, TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 5, 20)), ct);
        return text is null ? [] : FreeAssistPolicy.AcceptSearchTerms(query, ReadTerms(text));
    }

    public async Task<string?> CleanOcrTextAsync(string text, CancellationToken ct)
    {
        var source = (text ?? string.Empty).Trim();
        if (!CanAssistOcr || source.Length is < 8 or > FreeAssistPolicy.MaxOcrCharacters) return null;
        var prompt = "Corrija apenas erros evidentes de OCR no texto abaixo. Não acrescente fatos, nomes ou números. Se tiver dúvida, mantenha o trecho. Devolva só o texto corrigido.\n" + source;
        var rewritten = await CompleteAsync(prompt, TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 5, 20)), ct);
        return FreeAssistPolicy.AcceptOcrRewrite(source, rewritten);
    }

    private bool Ready(bool feature) =>
        _options.Enabled && feature
        && _options.Provider.Equals("Groq", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GROQ_API_KEY"))
        && _options.BaseUrl.StartsWith("https://api.groq.com/", StringComparison.OrdinalIgnoreCase);

    private async Task<string?> CompleteAsync(string prompt, TimeSpan timeout, CancellationToken ct)
    {
        var key = Environment.GetEnvironmentVariable("GROQ_API_KEY");
        if (string.IsNullOrWhiteSpace(key)) return null;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl.TrimEnd('/') + "/chat/completions");
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            var body = JsonSerializer.Serialize(new
            {
                model = _options.Model,
                messages = new[] { new { role = "user", content = prompt } },
                max_tokens = 600,
                temperature = 0
            });
            message.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, limit.Token);
            if (!response.IsSuccessStatusCode) return null;
            var payload = await response.Content.ReadAsStringAsync(limit.Token);
            if (payload.Length > 20_000) return null;
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A IA gratuita não respondeu. Nenhum conteúdo foi registrado.");
            return null;
        }
    }

    private static IEnumerable<string> ReadTerms(string text)
    {
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        if (start < 0 || end <= start) yield break;
        JsonDocument document;
        try { document = JsonDocument.Parse(text[start..(end + 1)]); }
        catch (JsonException) { yield break; }
        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array) yield break;
            foreach (var item in document.RootElement.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String && item.GetString() is string term)
                    yield return term;
        }
    }
}
