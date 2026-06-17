namespace InovaGed.Application.Common.Database;

public static class PostgresDateTimeHelper
{
    public static DateTimeOffset? ToUtc(DateTimeOffset? value)
    {
        if (value is null) return null;
        return value.Value.ToUniversalTime();
    }

    public static DateTimeOffset ToUtc(DateTimeOffset value)
    {
        return value.ToUniversalTime();
    }

    public static DateTimeOffset UtcNow()
    {
        return DateTimeOffset.UtcNow;
    }
}
