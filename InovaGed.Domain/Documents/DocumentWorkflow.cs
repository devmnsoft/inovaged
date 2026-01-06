using System;

namespace InovaGed.Domain.Documents;

public static class DocumentWorkflow
{
    public static bool CanTransition(DocumentStatus from, DocumentStatus to)
    {
        if (from == to) return false;

        return from switch
        {
            DocumentStatus.Draft => to is DocumentStatus.InReview,

            DocumentStatus.InReview => to is DocumentStatus.Draft
                                      or DocumentStatus.InSignature,

            DocumentStatus.InSignature => to is DocumentStatus.Published
                                         or DocumentStatus.InReview, // se quiser permitir “voltar”

            DocumentStatus.Published => to is DocumentStatus.Archived,

            DocumentStatus.Archived => false,

            _ => false
        };
    }

    public static bool RequiresReason(DocumentStatus from, DocumentStatus to)
    {
        // Motivo obrigatório em ações "críticas"
        // - enviar para análise
        // - reprovar (voltar)
        // - arquivar
        return (from, to) switch
        {
            (DocumentStatus.Draft, DocumentStatus.InReview) => true,
            (DocumentStatus.InReview, DocumentStatus.Draft) => true,
            (DocumentStatus.InReview, DocumentStatus.InSignature) => true,
            (DocumentStatus.Published, DocumentStatus.Archived) => true,
            _ => false
        };
    }
}
