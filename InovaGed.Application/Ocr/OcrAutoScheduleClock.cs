using System.Globalization;

namespace InovaGed.Application.Ocr;

public static class OcrAutoScheduleClock
{
    public static DateTimeOffset CalculateNextRun(DateTimeOffset nowUtc, string runAt, string timeZoneId)
    {
        var timeZone = ResolveTimeZone(timeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, timeZone);
        var localRunTime = ParseRunAt(runAt);
        var localRun = new DateTimeOffset(localNow.Date.Add(localRunTime), localNow.Offset);

        if (localRun <= localNow)
            localRun = new DateTimeOffset(localRun.Date.AddDays(1).Add(localRunTime), localNow.Offset);

        return TimeZoneInfo.ConvertTime(localRun, TimeZoneInfo.Utc);
    }

    public static string FormatLocal(DateTimeOffset utc, string timeZoneId)
    {
        var tz = ResolveTimeZone(timeZoneId);
        return TimeZoneInfo.ConvertTime(utc, tz).ToString("dd/MM/yyyy HH:mm zzz", CultureInfo.GetCultureInfo("pt-BR"));
    }

    public static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        var id = string.IsNullOrWhiteSpace(timeZoneId) ? "America/Belem" : timeZoneId.Trim();
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException) when (id.Equals("America/Belem", StringComparison.OrdinalIgnoreCase))
        {
            return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
        }
    }

    private static TimeSpan ParseRunAt(string? runAt)
        => TimeSpan.TryParseExact(runAt, @"hh\:mm", CultureInfo.InvariantCulture, out var value) ? value : new TimeSpan(18, 0, 0);
}
