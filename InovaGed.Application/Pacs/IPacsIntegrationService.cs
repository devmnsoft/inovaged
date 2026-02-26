    using InovaGed.Application.Pacs;

namespace InovaGed.Application.Pacs;

public interface IPacsIntegrationService
{
    Task<IReadOnlyList<PacsInboundFolderVM>> ListInboundFoldersAsync(CancellationToken ct);
    Task<IReadOnlyList<PacsInboundFileVM>> ListInboundFilesAsync(string folderName, CancellationToken ct);

    /// <summary>
    /// Cria o chamado e copia os arquivos para o storage do GED (SEMPRE no disco).
    /// Retorna o ID do Chamado criado.
    /// </summary>
    Task<Guid> CreateTicketFromFolderAsync(Guid tenantId, Guid userId, NewPacsTicketVM vm, CancellationToken ct);
}