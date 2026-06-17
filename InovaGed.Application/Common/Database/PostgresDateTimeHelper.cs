namespace InovaGed.Application.Common.Database;

public static class PostgresDateTimeHelper
{
    public static DateTimeOffset ToUtc(DateTimeOffset value)
    {
        return value.ToUniversalTime();
    }

    public static DateTimeOffset? ToUtc(DateTimeOffset? value)
    {
        if (value is null) return null;
        return ToUtc(value.Value);
    }

    public static DateTime ToUtc(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc)
            return value;

        if (value.Kind == DateTimeKind.Local)
            return value.ToUniversalTime();

        return DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    public static DateTime? ToUtc(DateTime? value)
    {
        if (!value.HasValue)
            return null;

        return ToUtc(value.Value);
    }

    public static DateTimeOffset StartOfDayUtc(DateTimeOffset value)
    {
        var utc = ToUtc(value);
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);
    }

    public static DateTime StartOfDayUtc(DateTime value)
    {
        var utc = ToUtc(value);
        return new DateTime(utc.Year, utc.Month, utc.Day, 0, 0, 0, DateTimeKind.Utc);
    }

    public static DateTimeOffset EndExclusiveDayUtc(DateTimeOffset value)
    {
        return StartOfDayUtc(value).AddDays(1);
    }

    public static DateTime EndExclusiveDayUtc(DateTime value)
    {
        return StartOfDayUtc(value).AddDays(1);
    }

    public static DateTime YearStartUtc(int year)
    {
        return new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    public static DateTime YearEndExclusiveUtc(int year)
    {
        return new DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    public static DateTimeOffset UtcNow()
    {
        return DateTimeOffset.UtcNow;
    }
}
