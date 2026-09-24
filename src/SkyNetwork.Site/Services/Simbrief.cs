using System.Globalization;
using System.Text.Json;
using SkyNetwork.Site.Data;

namespace SkyNetwork.Site.Services;

/// <summary>
/// Imports a pilot's latest SimBrief flight plan (OFP) by SimBrief username or Pilot ID:
/// the flight plan fields plus every route point with its coordinates, which the map draws.
/// </summary>
public sealed class Simbrief(IHttpClientFactory http, ILogger<Simbrief> log)
{
    public const int MaxWaypoints = 400;

    public async Task<(FlightPlan? Plan, string? Error)> FetchAsync(string user, CancellationToken ct)
    {
        user = user.Trim();
        if (user.Length is 0 or > 64) return (null, "Укажите имя пользователя или Pilot ID SimBrief");
        string query = user.All(char.IsAsciiDigit) ? "userid=" : "username=";
        string url = $"https://www.simbrief.com/api/xml.fetcher.php?{query}{Uri.EscapeDataString(user)}&json=v2";
        try
        {
            using var r = await http.CreateClient("simbrief").GetAsync(url, ct);
            // Unknown users and missing plans come back as 400 with a status text.
            return Parse(await r.Content.ReadAsStringAsync(ct));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            log.LogWarning("SimBrief {User}: {Error}", user, e.Message);
            return (null, "SimBrief не отвечает, попробуйте позже");
        }
    }

    /// <summary>Reads an OFP in SimBrief's JSON format (also used by tests).</summary>
    public static (FlightPlan? Plan, string? Error) Parse(string json)
    {
        JsonElement root;
        try { root = JsonDocument.Parse(json).RootElement; }
        catch (JsonException) { return (null, "SimBrief вернул непонятный ответ"); }
        if (root.ValueKind != JsonValueKind.Object) return (null, "SimBrief вернул непонятный ответ");

        string status = Str(root, "fetch", "status");
        if (!status.Equals("Success", StringComparison.OrdinalIgnoreCase))
            return (null, status.Contains("UserID", StringComparison.OrdinalIgnoreCase)
                ? "Такого пользователя SimBrief нет — проверьте имя или Pilot ID"
                : $"SimBrief: {(status.Length > 0 ? status : "план не найден")}");

        string dep = Str(root, "origin", "icao_code"), dest = Str(root, "destination", "icao_code");
        if (dep.Length != 4 || dest.Length != 4) return (null, "В последнем плане SimBrief нет аэродромов вылета и назначения");

        string callsign = Str(root, "atc", "callsign");
        if (callsign.Length == 0) callsign = Str(root, "general", "icao_airline") + Str(root, "general", "flight_number");
        string aircraft = Str(root, "aircraft", "icao_code");
        if (aircraft.Length == 0) aircraft = Str(root, "aircraft", "icaocode");
        int level = Int(Str(root, "general", "initial_altitude"));
        long outTime = (long)Num(Str(root, "times", "sched_out"));

        var plan = new FlightPlan
        {
            Callsign = callsign.ToUpperInvariant(),
            Rules = "IFR",
            Aircraft = aircraft.ToUpperInvariant(),
            CruiseSpeed = Int(Str(root, "general", "cruise_tas")),
            Departure = dep.ToUpperInvariant(),
            Destination = dest.ToUpperInvariant(),
            Alternate = Alternate(root).ToUpperInvariant(),
            DepartureTime = outTime > 0 ? DateTimeOffset.FromUnixTimeSeconds(outTime).UtcDateTime.ToString("HHmm", CultureInfo.InvariantCulture) : "",
            CruiseAltitude = level >= 10000 ? $"FL{level / 100:000}" : level > 0 ? level.ToString(CultureInfo.InvariantCulture) : "",
            EnrouteMinutes = (int)Math.Round(Int(Str(root, "times", "est_time_enroute")) / 60.0),
            FuelMinutes = (int)Math.Round(Int(Str(root, "times", "endurance")) / 60.0),
            Route = string.Join(' ', Str(root, "general", "route").Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant(),
            Waypoints = Waypoints(root),
        };
        return (plan, null);
    }

    /// <summary>Route points as JSON [[ident, lat, lon], …], departure and destination airports included.</summary>
    private static string Waypoints(JsonElement root)
    {
        var points = new List<(string Ident, double Lat, double Lon)>();
        void Add(string ident, string lat, string lon)
        {
            if (double.TryParse(lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var la)
                && double.TryParse(lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lo)
                && Math.Abs(la) <= 90 && Math.Abs(lo) <= 180
                && (points.Count == 0 || points[^1].Ident != ident || points[^1].Lat != la))
                points.Add((ident, la, lo));
        }
        Add(Str(root, "origin", "icao_code"), Str(root, "origin", "pos_lat"), Str(root, "origin", "pos_long"));
        if (root.TryGetProperty("navlog", out var navlog) && navlog.ValueKind == JsonValueKind.Object && navlog.TryGetProperty("fix", out var fixes))
            foreach (var f in fixes.ValueKind == JsonValueKind.Array ? fixes.EnumerateArray() : Enumerable.Repeat(fixes, 1))
                Add(Str(f, "ident"), Str(f, "pos_lat"), Str(f, "pos_long"));
        Add(Str(root, "destination", "icao_code"), Str(root, "destination", "pos_lat"), Str(root, "destination", "pos_long"));
        return Serialize(points);
    }

    public static string Serialize(IEnumerable<(string Ident, double Lat, double Lon)> points) =>
        JsonSerializer.Serialize(points.Take(MaxWaypoints).Select(p => new object[] { p.Ident, Math.Round(p.Lat, 4), Math.Round(p.Lon, 4) }));

    /// <summary>Route points sent back by the form: kept only when they are a well-formed list.</summary>
    public static string Clean(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "";
        try
        {
            var points = new List<(string, double, double)>();
            foreach (var p in JsonDocument.Parse(json).RootElement.EnumerateArray())
            {
                if (p.GetArrayLength() != 3) return "";
                string ident = p[0].GetString() ?? "";
                double lat = p[1].GetDouble(), lon = p[2].GetDouble();
                if (ident.Length > 12 || Math.Abs(lat) > 90 || Math.Abs(lon) > 180) return "";
                points.Add((ident.ToUpperInvariant(), lat, lon));
            }
            return points.Count is > 1 and <= MaxWaypoints ? Serialize(points) : "";
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException) { return ""; }
    }

    // A single alternate is an object, several are an array.
    private static string Alternate(JsonElement root)
    {
        if (!root.TryGetProperty("alternate", out var a)) return "";
        if (a.ValueKind == JsonValueKind.Array) a = a.GetArrayLength() > 0 ? a[0] : default;
        return a.ValueKind == JsonValueKind.Object ? Str(a, "icao_code") : "";
    }

    private static string Str(JsonElement e, params string[] path)
    {
        foreach (var name in path)
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out e)) return "";
        return e.ValueKind switch
        {
            JsonValueKind.String => e.GetString()!.Trim(),
            JsonValueKind.Number => e.GetRawText(),
            _ => "",
        };
    }

    private static double Num(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static int Int(string s) => (int)Math.Round(Num(s));
}
