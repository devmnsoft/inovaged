using System.Net.Http.Json;
using System.Text.Json;
using InovaGed.Application.Sectors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InovaGed.Infrastructure.Sectors;

public sealed class ConfigurableClinicalSource : IClinicalSource
{
    private readonly HttpClient _http;
    private readonly ClinicalSourceOptions _options;
    private readonly ILogger<ConfigurableClinicalSource> _logger;

    public ConfigurableClinicalSource(HttpClient http, IOptions<ClinicalSourceOptions> options, ILogger<ConfigurableClinicalSource> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(15);
    }

    public Task<ClinicalSourceResult> FindAsync(Guid tenantId, string externalCode, string? sourceSystem, string? sourceUnit, string identifierKind, CancellationToken ct)
        => SendAsync(tenantId, _options.PatientPath, new Dictionary<string, string?>
        {
            ["codigo"] = externalCode,
            ["tipoIdentificador"] = identifierKind,
            ["sistema"] = sourceSystem,
            ["unidade"] = sourceUnit
        }, async response =>
        {
            var body = await response.Content.ReadFromJsonAsync<PatientPayload>(cancellationToken: ct);
            if (body is null || !string.Equals(body.Status, "encontrado", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(body.Codigo))
                return new ClinicalSourceResult(ClinicalSourceStatus.NotFound, ClinicalSourceMessages.NotFound, null, []);
            var encounters = (body.Atendimentos ?? []).Select(item => new ExternalEncounter(item.Codigo ?? "", item.TipoExterno, item.Entrada, item.Alta, item.Situacao)).ToArray();
            return new ClinicalSourceResult(ClinicalSourceStatus.Found, "Paciente localizado na origem configurada.", new ExternalPatient(sourceSystem ?? "MV", sourceUnit, body.Codigo, body.Nome ?? body.Codigo), encounters);
        }, ct);

    public Task<ClinicalDocumentResult> ListDocumentsAsync(Guid tenantId, string? sourceSystem, string? sourceUnit, string patientCode, string encounterCode, CancellationToken ct)
        => SendAsync(tenantId, _options.DocumentsPath, new Dictionary<string, string?>
        {
            ["codigoPaciente"] = patientCode,
            ["codigoAtendimento"] = encounterCode,
            ["sistema"] = sourceSystem,
            ["unidade"] = sourceUnit
        }, async response =>
        {
            var body = await response.Content.ReadFromJsonAsync<DocumentPayload>(cancellationToken: ct);
            var docs = (body?.Documentos ?? []).Where(item => !string.IsNullOrWhiteSpace(item.Id)).Select(item => new ClinicalDocumentRef(item.Id!, item.Nome, item.Situacao, item.AtualizadoEm)).ToArray();
            return new ClinicalDocumentResult(ClinicalSourceStatus.Found, "Metadados recebidos da origem configurada.", docs);
        }, ct);

    public Task<ClinicalContentResult> OpenContentAsync(Guid tenantId, string externalDocumentId, CancellationToken ct)
        => SendAsync(tenantId, _options.ContentPath, new Dictionary<string, string?> { ["documentoExterno"] = externalDocumentId }, async response =>
        {
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            var stream = new MemoryStream(bytes, writable: false);
            return new ClinicalContentResult(ClinicalSourceStatus.Found, "Conteúdo recebido da origem configurada.", stream, "documento-origem.pdf", response.Content.Headers.ContentType?.MediaType);
        }, ct);

    private async Task<T> SendAsync<T>(Guid tenantId, string? path, IReadOnlyDictionary<string, string?> query, Func<HttpResponseMessage, Task<T>> ok, CancellationToken ct)
        where T : class
    {
        var blocked = Missing();
        if (blocked is not null)
        {
            _logger.LogInformation("Origem clínica indisponível. Tenant={TenantId} Motivo={Motivo}", tenantId, blocked);
            return Unavailable<T>(blocked);
        }

        if (!TryBuildUri(path, query, out var target, out var uriError))
        {
            _logger.LogInformation("Destino da origem clínica recusado. Tenant={TenantId} Motivo={Motivo}", tenantId, uriError);
            return Unavailable<T>(uriError!, ClinicalSourceStatus.Failed);
        }
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, target);
            var keyName = _options.ApiKeyEnvironmentVariable;
            var key = string.IsNullOrWhiteSpace(keyName) ? null : Environment.GetEnvironmentVariable(keyName);
            if (!string.IsNullOrWhiteSpace(key)) request.Headers.TryAddWithoutValidation("X-InovaGed-Origem", key);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                _logger.LogWarning("Redirecionamento da origem clínica recusado. Tenant={TenantId}", tenantId);
                return Unavailable<T>("A origem clínica respondeu com redirecionamento. A credencial não foi enviada a outro destino.", ClinicalSourceStatus.Failed);
            }
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return Unavailable<T>(ClinicalSourceMessages.NotFound, ClinicalSourceStatus.NotFound);
            if (!response.IsSuccessStatusCode)
                return Unavailable<T>(ClinicalSourceMessages.Failed, ClinicalSourceStatus.Failed);
            return await ok(response);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning("Falha ao consultar a origem clínica configurada. Tenant={TenantId} Tipo={Tipo}", tenantId, ex.GetType().Name);
            return Unavailable<T>(ClinicalSourceMessages.Unavailable, ClinicalSourceStatus.Unavailable);
        }
    }

    private bool TryBuildUri(string? path, IReadOnlyDictionary<string, string?> query, out Uri target, out string? error)
    {
        target = null!;
        error = null;
        if (string.IsNullOrWhiteSpace(path) || path.Contains("://", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
        {
            error = "O caminho da origem clínica precisa ser relativo ao host configurado.";
            return false;
        }
        var baseUri = new Uri(_options.BaseUrl!);
        var built = new Uri(baseUri, path);
        if (!string.Equals(built.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase) || built.Port != baseUri.Port || !string.Equals(built.Scheme, baseUri.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            error = "O caminho da origem clínica apontaria para outro destino. A consulta foi recusada.";
            return false;
        }
        var builder = new UriBuilder(built)
        {
            Query = string.Join('&', query.Where(item => !string.IsNullOrWhiteSpace(item.Value)).Select(item => Uri.EscapeDataString(item.Key) + "=" + Uri.EscapeDataString(item.Value!)))
        };
        target = builder.Uri;
        return true;
    }

    private string? Missing()
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.BaseUrl) || string.IsNullOrWhiteSpace(_options.PatientPath))
            return "A origem clínica não está configurada. Informe ClinicalSource:Enabled, BaseUrl e PatientPath conforme o contrato publicado. Nenhum paciente foi simulado.";
        if (!Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out _))
            return "A URL base da origem clínica é inválida. Nenhum paciente foi simulado.";
        return null;
    }

    private static T Unavailable<T>(string message, ClinicalSourceStatus status = ClinicalSourceStatus.Unavailable) where T : class
    {
        if (typeof(T) == typeof(ClinicalSourceResult))
            return (T)(object)new ClinicalSourceResult(status, message, null, []);
        if (typeof(T) == typeof(ClinicalDocumentResult))
            return (T)(object)new ClinicalDocumentResult(status, message, []);
        return (T)(object)new ClinicalContentResult(status, message, null, null, null);
    }

    private sealed class PatientPayload
    {
        public string? Status { get; set; }
        public string? Codigo { get; set; }
        public string? Nome { get; set; }
        public EncounterPayload[]? Atendimentos { get; set; }
    }

    private sealed class EncounterPayload
    {
        public string? Codigo { get; set; }
        public string? TipoExterno { get; set; }
        public DateTimeOffset? Entrada { get; set; }
        public DateTimeOffset? Alta { get; set; }
        public string? Situacao { get; set; }
    }

    private sealed class DocumentPayload
    {
        public DocumentItem[]? Documentos { get; set; }
    }

    private sealed class DocumentItem
    {
        public string? Id { get; set; }
        public string? Nome { get; set; }
        public string? Situacao { get; set; }
        public DateTimeOffset? AtualizadoEm { get; set; }
    }
}
