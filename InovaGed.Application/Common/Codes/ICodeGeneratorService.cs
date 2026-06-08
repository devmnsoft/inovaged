namespace InovaGed.Application.Common.Codes;

public interface ICodeGeneratorService
{
    Task<string> GenerateNextCodeAsync(
        Guid tenantId,
        string entityName,
        string? prefix,
        CancellationToken ct);

    Task<string> GenerateNextNumericCodeAsync(
        Guid tenantId,
        string entityName,
        int padding,
        CancellationToken ct);
}
