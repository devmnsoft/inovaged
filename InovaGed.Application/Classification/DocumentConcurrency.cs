namespace InovaGed.Application.Classification;

public static class DocumentConcurrency
{
    public const string ConflictCode = "CONFLICT";
    public const string MissingTokenCode = "CONCURRENCY_TOKEN_REQUIRED";
    public const string ConflictMessage = "Este documento foi alterado por outra pessoa. Atualize a tela. A gravação já confirmada foi preservada.";
    public const string MissingTokenMessage = "A edição precisa da versão do documento exibida na tela.";

    public static string TokenSelect(string alias = "")
    {
        var prefix = string.IsNullOrEmpty(alias) ? string.Empty : alias + ".";
        return $"{prefix}xmin::text::bigint";
    }
}
