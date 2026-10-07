namespace InovaGed.Application.Ged.Documents;

public sealed record DocumentUploadSizeLimit(long? MaxBytes, string DisplayText)
{
    public bool HasBusinessLimit => MaxBytes is > 0;
}

public static class DocumentUploadSizePolicy
{
    public static DocumentUploadSizeLimit Resolve(DocumentUploadOptions options)
    {
        if (options.MaxFileSizeBytes.HasValue)
        {
            if (options.MaxFileSizeBytes.Value < 0)
                throw new InvalidOperationException("DocumentUpload:MaxFileSizeBytes não pode ser negativo.");

            return options.MaxFileSizeBytes.Value == 0
                ? new DocumentUploadSizeLimit(null, "Sem limite de negócio configurado")
                : new DocumentUploadSizeLimit(options.MaxFileSizeBytes.Value, FormatBytes(options.MaxFileSizeBytes.Value));
        }

        if (options.MaxFileSizeMb < 0)
            throw new InvalidOperationException("DocumentUpload:MaxFileSizeMb não pode ser negativo.");

        return options.MaxFileSizeMb == 0
            ? new DocumentUploadSizeLimit(null, "Sem limite de negócio configurado")
            : new DocumentUploadSizeLimit(options.MaxFileSizeMb * 1024L * 1024L, $"{options.MaxFileSizeMb} MB");
    }

    public static bool Exceeds(DocumentUploadOptions options, long sizeBytes, out DocumentUploadSizeLimit limit)
    {
        limit = Resolve(options);
        return limit.MaxBytes.HasValue && sizeBytes > limit.MaxBytes.Value;
    }

    public static string FormatBytes(long bytes)
        => bytes % (1024L * 1024L) == 0 ? $"{bytes / (1024L * 1024L)} MB" : $"{bytes} bytes";
}
