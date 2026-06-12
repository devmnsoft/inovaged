namespace InovaGed.Infrastructure.Sql;

public sealed record SqlSafetyValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new InvalidOperationException("SQL dinâmico inválido: " + string.Join("; ", Errors));
        }
    }
}
