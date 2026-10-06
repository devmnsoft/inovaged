namespace InovaGed.Application.Protocolo;

public interface IProtocolAiAssistService
{
    Task<ProtocolAiAssistResultDto> AssistAsync(ProtocolAiAssistRequest request, CancellationToken ct);
    Task<ProtocolAiApplyResultDto> ApplySubjectAsync(ProtocolAiApplySubjectRequest request, CancellationToken ct);
    Task<ProtocolAiApplyResultDto> ApplyDispatchDraftAsync(ProtocolAiApplyDraftRequest request, CancellationToken ct);
    Task<ProtocolAiHistoryDto> GetReviewHistoryAsync(Guid protocoloId, int page, int pageSize, CancellationToken ct);
}
