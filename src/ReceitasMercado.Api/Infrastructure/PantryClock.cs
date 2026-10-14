namespace ReceitasMercado.Api.Infrastructure;

public static class PantryClock
{
    public static DateOnly Today(string fuso, DateTimeOffset now)
    {
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(fuso); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { zone = TimeZoneInfo.Utc; }
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
    }

    public static bool TryParseCursor(string? cursor, out int offset)
    {
        offset = 0;
        return string.IsNullOrEmpty(cursor) || (int.TryParse(cursor, out offset) && offset >= 0);
    }
}
