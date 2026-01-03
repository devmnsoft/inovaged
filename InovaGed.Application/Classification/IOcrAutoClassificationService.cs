using InovaGed.Application.Identity;

namespace InovaGed.Application.Classification;

public interface IOcrAutoClassificationService
{
    Task RunAsync(Guid tenantId, Guid documentId, Guid ocrVersionId, CancellationToken ct);
}

public sealed class OcrAutoClassificationService : IOcrAutoClassificationService
{
    private readonly ICurrentUser _currentUser;
    private readonly DocumentClassificationAppService _app;

    public OcrAutoClassificationService(
        ICurrentUser currentUser,
        DocumentClassificationAppService app)
    {
        _currentUser = currentUser;
        _app = app;
    }

    public async Task RunAsync(
        Guid tenantId,
        Guid documentId,
        Guid ocrVersionId,
        CancellationToken ct)
    {
        // 🔒 Segurança mínima
        if (!_currentUser.IsAuthenticated)
            throw new InvalidOperationException("Usuário não autenticado para auto-classificação.");

        // 🔐 Garante consistência multi-tenant
        if (_currentUser.TenantId != tenantId)
            throw new InvalidOperationException("Tenant inválido para auto-classificação.");

        /*
         * ✅ Fluxo REAL do seu sistema:
         * - Lê OCR/texto da ÚLTIMA versão (ocr_text / content_text)
         * - Executa SimpleTextDocumentTypeSuggester
         * - Salva sugestão via _repo.SetSuggestionAsync(...)
         * - Auto-aplica se confiança >= 0.80 e ainda não classificado
         *
         * Tudo isso já existe no AppService.
         */
        await _app.SuggestByLatestVersionTextAsync(documentId, ct);
    }
}
