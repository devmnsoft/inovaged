using System.Text;
using InovaGed.Application.Common.Storage;
using InovaGed.Application.Ged.Protocols;
using InovaGed.Domain.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace InovaGed.Web.Common;

/// <summary>
/// Resultado de falha por arquivo em anexos de protocolo (arquivo, etapa, motivo, retryability, correlação).
/// Exceções/detalhes técnicos permanecem apenas no log; a tela recebe mensagem segura.
/// </summary>
public sealed record ProtocolFileResult(
    string FileName,
    string Step,
    string Reason,
    bool CanRetry,
    string CorrelationId);

/// <summary>
/// Grava anexos de protocolo com validação de nome, tratamento por arquivo,
/// verificação do resultado de AddAttachmentAsync e compensação de órfãos de storage.
/// </summary>
public static class ProtocolAttachmentSaver
{
    public static async Task<List<ProtocolFileResult>> SaveAttachmentsAsync(
        IFileStorage storage,
        IProtocolRequestService service,
        Guid tenantId,
        Guid protocolId,
        Guid userId,
        IReadOnlyList<IFormFile> files,
        ILogger logger,
        CancellationToken ct)
    {
        var results = new List<ProtocolFileResult>();
        if (files is null || files.Count == 0) return results;

        foreach (var file in files.Where(f => f is not null && f.Length > 0))
        {
            var original = string.IsNullOrWhiteSpace(file.FileName) ? "arquivo" : file.FileName;
            var safe = SanitizeFileName(original);
            if (!string.Equals(safe, SanitizeFileName(Path.GetFileName(original)), StringComparison.Ordinal))
                logger.LogInformation("Anexo do protocolo {Protocol} com nome ajustado: '{Orig}' -> '{Safe}'.", protocolId, original, safe);

            var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
            var path = $"protocols/{tenantId:N}/{protocolId:N}/{Guid.NewGuid():N}_{safe}";

            try
            {
                await using var stream = file.OpenReadStream();
                await storage.SaveDerivedAsync(path, stream, contentType, ct);
            }
            catch (Exception ex)
            {
                await TryCompensateAsync(storage, path, protocolId, logger, ex);
                logger.LogError(ex, "Falha ao armazenar anexo do protocolo {Protocol} ({File}).", protocolId, original);
                results.Add(new ProtocolFileResult(original, "Armazenamento do arquivo",
                    "Não foi possível salvar o arquivo. Revise o nome/tamanho e tente enviar novamente.", true, Guid.NewGuid().ToString("N")));
                continue;
            }

            Result res;
            try
            {
                res = await service.AddAttachmentAsync(tenantId, protocolId, userId, safe, contentType, file.Length, path, ct);
            }
            catch (Exception ex)
            {
                await TryCompensateAsync(storage, path, protocolId, logger, ex);
                logger.LogError(ex, "Falha ao registrar anexo do protocolo {Protocol} ({File}).", protocolId, original);
                results.Add(new ProtocolFileResult(original, "Registro no protocolo",
                    "O arquivo foi gravado, mas não pôde ser vinculado ao protocolo. Tente enviar novamente.", true, Guid.NewGuid().ToString("N")));
                continue;
            }

            if (!res.IsSuccess)
            {
                await TryCompensateAsync(storage, path, protocolId, logger, null);
                logger.LogWarning("Anexo do protocolo {Protocol} rejeitado ({File}): {Msg}", protocolId, original, res.ErrorMessage);
                results.Add(new ProtocolFileResult(original, "Registro no protocolo",
                    res.ErrorMessage ?? "Não foi possível vincular o arquivo ao protocolo.", false, Guid.NewGuid().ToString("N")));
            }
        }

        return results;
    }

    /// <summary>Remove caracteres inválidos para o sistema operacional e normaliza nomes vazios.</summary>
    public static string SanitizeFileName(string name)
    {
        name = Path.GetFileName(name ?? string.Empty);
        if (name.Length == 0) name = "arquivo";
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
            sb.Append(invalid.Contains(ch) ? '_' : ch);
        var clean = sb.ToString().Trim().TrimEnd('.');
        return clean.Length == 0 ? "anexo" : clean;
    }

    private static async Task TryCompensateAsync(IFileStorage storage, string path, Guid protocolId, ILogger logger, Exception? cause)
    {
        try
        {
            await storage.DeleteIfExistsAsync(path, CancellationToken.None);
        }
        catch (Exception delEx)
        {
            logger.LogError(delEx, "Não foi possível compensar (excluir) arquivo órfão {Path} do protocolo {Protocol}.", path, protocolId);
        }
        if (cause is not null)
            logger.LogDebug(cause, "Erro original durante gravação do anexo do protocolo {Protocol}.", protocolId);
    }
}
