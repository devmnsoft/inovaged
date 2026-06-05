using System.Globalization;

namespace InovaGed.Web.Services;

public sealed class DateTimeDisplayService : IDateTimeDisplayService
{
    private readonly IConfiguration _configuration;

    public DateTimeDisplayService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string FormatUploadDate(DateTime? uploadedAtUtc)
    {
        if (!uploadedAtUtc.HasValue || uploadedAtUtc.Value == default)
        {
            return string.Empty;
        }

        var utc = uploadedAtUtc.Value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(uploadedAtUtc.Value, DateTimeKind.Utc)
            : uploadedAtUtc.Value.ToUniversalTime();

        var timeZoneId = _configuration["Localization:DefaultTimeZone"]
            ?? _configuration["App:LocalTimeZoneId"]
            ?? _configuration["TimeZoneId"]
            ?? "America/Belem";
        var format = _configuration["Localization:DateTimeFormat"] ?? "dd/MM/yyyy HH:mm";

        try
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return TimeZoneInfo.ConvertTimeFromUtc(utc, timeZone).ToString(format, CultureInfo.GetCultureInfo("pt-BR"));
        }
        catch (TimeZoneNotFoundException)
        {
            return utc.ToLocalTime().ToString(format, CultureInfo.GetCultureInfo("pt-BR"));
        }
        catch (InvalidTimeZoneException)
        {
            return utc.ToLocalTime().ToString(format, CultureInfo.GetCultureInfo("pt-BR"));
        }
    }
}
