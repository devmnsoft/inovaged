namespace InovaGed.Application.Sectors;

public interface ISectorOpeningService
{
    Task<SectorOpeningResponse> OpenAsync(SectorOpeningRequest request, CancellationToken ct);
    Task<SectorConsumedOpening?> ConsumeAsync(string code, CancellationToken ct);
    Task<SectorSessionAccess?> ReadSessionAsync(string? cookieValue, Guid publicId, CancellationToken ct);
    Task<bool> SwitchEncounterAsync(string? cookieValue, Guid publicId, string encounterCode, CancellationToken ct);
    Task<bool> AuthorizeCommandAsync(string? cookieValue, Guid publicId, string? protectionToken, CancellationToken ct);
    Task<string?> RegisterIntegrationAsync(SectorIntegrationRegistration registration, CancellationToken ct);
    Task<string?> DeactivateIntegrationAsync(Guid tenantId, Guid actorId, string clientId, CancellationToken ct);
}

public static class SectorOpeningCodes
{
    public const string Unauthorized = "integracao_nao_autorizada";
    public const string Invalid = "solicitacao_invalida";
    public const string Unavailable = "origem_indisponivel";
    public const string PatientMissing = "paciente_nao_encontrado";
    public const string EncounterMismatch = "atendimento_divergente";
    public const string DocumentMissing = "documento_ausente";
    public const string Several = "varias_ocorrencias";
    public const string Denied = "acesso_negado";
    public const string Opened = "abertura_concedida";
}

public static class SectorOpeningLink
{
    public static string Path(string code) => "/DemaisSetores/Abrir/" + code;

    public static bool Exposes(string url, string sensitive)
        => !string.IsNullOrEmpty(sensitive) && url.Contains(sensitive, StringComparison.OrdinalIgnoreCase);
}

public sealed record SectorOpeningRequest(
    string? ClientId,
    string? Secret,
    string? IdentifierKind,
    string? Identifier,
    string? SourceSystem,
    string? SourceUnit,
    string? EncounterCode,
    string? DocumentName,
    string? DocumentCode,
    Guid? DocumentId,
    Guid? VersionId,
    string? Mode,
    string? Origin);

public sealed record SectorOpeningResponse(
    bool Accepted,
    string Code,
    string Message,
    string? PageUrl,
    string? FrameUrl,
    DateTimeOffset? ExpiresAt,
    string? Scope,
    string? CorrelationId,
    int Occurrences);

public sealed record SectorConsumedOpening(
    string CookieValue,
    Guid PublicId,
    Guid TenantId,
    Guid PatientId,
    Guid EncounterId,
    string? CatalogCode,
    Guid? DocumentId,
    Guid? VersionId,
    string Mode,
    string ScopeKind,
    string Origin,
    DateTimeOffset ExpiresAt,
    string CorrelationId,
    string ProtectionToken);

public sealed record SectorSessionAccess(
    Guid PublicId,
    Guid TenantId,
    Guid PatientId,
    Guid EncounterId,
    string? CatalogCode,
    Guid? DocumentId,
    Guid? VersionId,
    string Mode,
    string ScopeKind,
    bool AllowEncounterSwitch,
    string Origin,
    string CorrelationId,
    DateTimeOffset ExpiresAt);

public sealed record SectorIntegrationRegistration(
    Guid TenantId,
    Guid ActorId,
    string ClientId,
    string Secret,
    string SourceSystem,
    string? SourceUnit,
    string[] Origins,
    string[] CatalogCodes,
    bool AllowEncounterList,
    bool AllowEncounterSwitch,
    int MaxMinutes);
