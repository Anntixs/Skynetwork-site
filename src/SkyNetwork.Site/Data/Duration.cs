using System.Globalization;

namespace SkyNetwork.Site.Data;

/// <summary>
/// Flight plan durations (time en route, fuel) as pilots write them: "02:20", "2:20" or the ICAO "0220" all mean
/// 2 h 20 min; a bare small number ("90") is minutes. Stored as minutes.
/// </summary>
public static class Duration
{
    public static bool TryParseMinutes(string? text, out int minutes)
    {
        minutes = 0;
        string s = (text ?? "").Trim().Replace('.', ':').Replace('ч', ':').Replace('h', ':');
        if (s.Length == 0) return false;
        int colon = s.IndexOf(':');
        if (colon >= 0)
        {
            string h = s[..colon].Trim(), m = s[(colon + 1)..].Trim();
            if (m.Length == 0) m = "0";
            if (h.Length == 0) h = "0";
            if (!int.TryParse(h, NumberStyles.None, CultureInfo.InvariantCulture, out var hours) ||
                !int.TryParse(m, NumberStyles.None, CultureInfo.InvariantCulture, out var mins) || mins > 59) return false;
            minutes = hours * 60 + mins;
            return true;
        }
        if (!s.All(char.IsAsciiDigit)) return false;
        if (s.Length <= 2)
        {
            minutes = int.Parse(s, CultureInfo.InvariantCulture);
            return true;
        }
        if (s.Length > 4) return false;
        // "0220" / "220": hours then minutes, as in an ICAO flight plan.
        int hh = int.Parse(s[..^2], CultureInfo.InvariantCulture), mm = int.Parse(s[^2..], CultureInfo.InvariantCulture);
        if (mm > 59) return false;
        minutes = hh * 60 + mm;
        return true;
    }

    /// <summary>140 → "02:20"; 0 → "".</summary>
    public static string Format(int minutes) =>
        minutes <= 0 ? "" : $"{minutes / 60:00}:{minutes % 60:00}";
}
