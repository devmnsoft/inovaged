namespace InovaGed.Web.Services;

public static class ProtocolOriginLinkRules
{
    public const string UnavailableDocument = "O documento de origem não está disponível. O protocolo não foi criado.";
    public const string EditRequired = "Criar o protocolo deste documento exige permissão para consultar e editar o documento. O protocolo não foi criado.";
    public const string LinkNotSaved = "O protocolo não foi criado porque o vínculo com o documento de origem não pôde ser gravado.";
    public const string Requirement = "Este protocolo só será criado se o vínculo com o documento de origem for gravado na mesma operação. É necessário poder consultar e editar o documento.";

    public static bool RequestsLink(Guid? documentId) => documentId is Guid id && id != Guid.Empty;

    public static string? BlockReason(bool documentActive, bool canView, bool canEdit)
    {
        if (!documentActive) return UnavailableDocument;
        if (!canView || !canEdit) return EditRequired;
        return null;
    }
}
