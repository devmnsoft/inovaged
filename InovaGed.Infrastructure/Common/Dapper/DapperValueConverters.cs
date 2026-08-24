namespace InovaGed.Infrastructure.Common.Dapper;

/// <summary>Normalizes provider values before they are exposed through application contracts.</summary>
public static class DapperValueConverters
{
    public static DateTimeOffset? ToDateTimeOffset(DateTime? value)
    {
        if (!value.HasValue) return null;

        var date = value.Value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            : value.Value;
        return new DateTimeOffset(date);
    }

    public static DateTimeOffset ToDateTimeOffsetRequired(DateTime? value) =>
        ToDateTimeOffset(value) ?? DateTimeOffset.UtcNow;

    public static TEnum ParseEnumOrDefault<TEnum>(string? value, TEnum fallback)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : fallback;

    public static string TextOrDefault(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
