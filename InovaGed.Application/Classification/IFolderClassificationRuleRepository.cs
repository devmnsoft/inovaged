namespace InovaGed.Application.Classification;

public interface IFolderClassificationRuleRepository
{
    /// <summary>
    /// Retorna o tipo documental padrão configurado para a pasta (se houver).
    /// </summary>
    Task<Guid?> GetDefaultTypeAsync(Guid tenantId, Guid folderId, CancellationToken ct);

    /// <summary>
    /// Define tipo documental padrão da pasta (ou remove se documentTypeId = null).
    /// </summary>
    Task SetDefaultTypeAsync(Guid tenantId, Guid folderId, Guid? documentTypeId, Guid? userId, CancellationToken ct);

    /// <summary>
    /// Verifica se o documento tem classificação (document_type_id preenchido).
    /// </summary>
    Task<bool> HasClassificationAsync(Guid tenantId, Guid documentId, CancellationToken ct);

    /// <summary>
    /// Atualiza sugestão (tipo/score/método/when) para um documento.
    /// </summary>
    Task SetSuggestionAsync(
        Guid tenantId,
        Guid documentId,
        Guid? suggestedTypeId,
        decimal? confidence,
        string? method,
        DateTimeOffset? suggestedAt,
        CancellationToken ct);

    /// <summary>
    /// Conta documentos não classificados (opcional filtrar por pasta).
    /// </summary>
    Task<int> CountUnclassifiedAsync(Guid tenantId, Guid? folderId, CancellationToken ct);
}
