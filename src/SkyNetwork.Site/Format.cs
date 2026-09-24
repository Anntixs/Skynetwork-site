using System.Globalization;

namespace SkyNetwork.Site;

/// <summary>Formatting shared by pages: everything on the network runs in UTC.</summary>
public static class Format
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public static string Utc(DateTime t) => t.ToString("dd.MM.yyyy HH:mm", Ru) + "z";
    public static string Date(DateTime t) => t.ToString("d MMMM yyyy", Ru);
    public static string DayMonth(DateTime t) => t.ToString("d MMM", Ru).TrimEnd('.');
    public static string Time(DateTime t) => t.ToString("HH:mm", Ru) + "z";
    public static string Span(DateTime a, DateTime b) =>
        a.Date == b.Date ? $"{DayMonth(a)}, {Time(a)}–{Time(b)}" : $"{DayMonth(a)} {Time(a)} — {DayMonth(b)} {Time(b)}";

    public static string Hours(double hours) => hours.ToString(hours < 10 ? "0.0" : "0", CultureInfo.InvariantCulture) + " ч";

    public static string Duration(TimeSpan d) => d.TotalHours >= 1 ? $"{(int)d.TotalHours} ч {d.Minutes:00} мин" : $"{d.Minutes} мин";

    public static string Initials(string name) =>
        string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));

    public static long? ParseUtc(string? date, string? time)
    {
        if (string.IsNullOrWhiteSpace(date) || string.IsNullOrWhiteSpace(time)) return null;
        return DateTime.TryParseExact($"{date} {time}", "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)
            ? new DateTimeOffset(t, TimeSpan.Zero).ToUnixTimeSeconds() : null;
    }

    public static string InputDate(DateTime t) => t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static string InputTime(DateTime t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);
}
