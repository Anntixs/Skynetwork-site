using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkyNetwork.Site.Data;

namespace SkyNetwork.Site.Services;

/// <summary>
/// Imports a pilot's latest SimBrief flight plan (OFP) by SimBrief username or Pilot ID: the flight plan fields, every
/// route point with its coordinates and airway, and a few extras the map card shows (registration, step climbs, …).
/// </summary>
public sealed class Simbrief(IHttpClientFactory http, NavData nav, ILogger<Simbrief> log)
{
    public const int MaxWaypoints = 600;
    private static readonly TimeSpan RouteCache = TimeSpan.FromMinutes(10);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime At, FlightPlan? Plan)> _latest = new(StringComparer.OrdinalIgnoreCase);

    public async Task<(FlightPlan? Plan, string? Error)> FetchAsync(string user, CancellationToken ct)
    {
        user = user.Trim();
        if (user.Length is 0 or > 64) return (null, "Enter your SimBrief username or Pilot ID");
        string query = user.All(char.IsAsciiDigit) ? "userid=" : "username=";
        string url = $"https://www.simbrief.com/api/xml.fetcher.php?{query}{Uri.EscapeDataString(user)}&json=v2";
        try
        {
            using var r = await http.CreateClient("simbrief").GetAsync(url, ct);
            // Unknown users and missing plans come back as 400 with a status text.
            var result = Parse(await r.Content.ReadAsStringAsync(ct));
            // Every imported route teaches the site current waypoints and airways.
            if (result.Plan != null && StoredRoute.Parse(result.Plan.Waypoints) is { } stored)
                nav.Learn(stored.Points, result.Plan.Departure, result.Plan.Destination);
            return result;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            log.LogWarning("SimBrief {User}: {Error}", user, e.Message);
            return (null, "SimBrief is not answering, try again later");
        }
    }

    /// <summary>
    /// The member's latest SimBrief plan when it is this flight (same departure and destination), for pilots who plan
    /// in SimBrief but file some other way. One request per member every 10 minutes at most.
    /// </summary>
    public async Task<string?> RouteForAsync(string user, string departure, string destination, CancellationToken ct)
    {
        if (user.Length == 0) return null;
        if (!_latest.TryGetValue(user, out var hit) || DateTime.UtcNow - hit.At > RouteCache)
        {
            var (plan, _) = await FetchAsync(user, ct);
            _latest[user] = hit = (DateTime.UtcNow, plan);
            if (_latest.Count > 2000) _latest.Clear();
        }
        return hit.Plan is { Waypoints.Length: > 0 } p
            && p.Departure.Equals(departure, StringComparison.OrdinalIgnoreCase) && p.Destination.Equals(destination, StringComparison.OrdinalIgnoreCase)
            ? p.Waypoints : null;
    }

    /// <summary>Reads an OFP in SimBrief's JSON format (also used by tests).</summary>
    public static (FlightPlan? Plan, string? Error) Parse(string json)
    {
        JsonElement root;
        try { root = JsonDocument.Parse(json).RootElement; }
        catch (JsonException) { return (null, "SimBrief sent an answer we cannot read"); }
        if (root.ValueKind != JsonValueKind.Object) return (null, "SimBrief sent an answer we cannot read");

        string status = Str(root, "fetch", "status");
        if (!status.Equals("Success", StringComparison.OrdinalIgnoreCase))
            return (null, status.Contains("UserID", StringComparison.OrdinalIgnoreCase)
                ? "There is no such SimBrief user: check the username or Pilot ID"
                : status.Length > 0 ? $"SimBrief: {status}" : "SimBrief has no plan for this user");

        string dep = Str(root, "origin", "icao_code"), dest = Str(root, "destination", "icao_code");
        if (dep.Length != 4 || dest.Length != 4) return (null, "The latest SimBrief plan has no departure or destination airport");

        string airline = Str(root, "general", "icao_airline"), flight = Str(root, "general", "flight_number");
        string callsign = Str(root, "atc", "callsign");
        if (callsign.Length == 0) callsign = airline + flight;
        string aircraft = Str(root, "aircraft", "icao_code");
        if (aircraft.Length == 0) aircraft = Str(root, "aircraft", "icaocode");
        int level = Int(Str(root, "general", "initial_altitude"));
        long outTime = (long)Num(Str(root, "times", "sched_out"));

        var stored = new StoredRoute
        {
            Airline = airline.ToUpperInvariant(),
            Flight = flight,
            Registration = Str(root, "aircraft", "reg").ToUpperInvariant(),
            AircraftName = Str(root, "aircraft", "name"),
            StepClimbs = Str(root, "general", "stepclimb_string"),
            CruiseMach = Str(root, "general", "cruise_mach"),
            Airac = Str(root, "params", "airac"),
            RouteDistance = Int(Str(root, "general", "route_distance")),
            OffTime = (long)Num(Str(root, "times", "sched_off")),
            OnTime = (long)Num(Str(root, "times", "sched_on")),
        };
        Points(root, stored.Points);

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
            Waypoints = stored.Points.Count > 1 ? stored.ToJson() : "",
        };
        return (plan, null);
    }

    /// <summary>Departure, the navlog fixes (top of climb and descent left out) and the destination.</summary>
    private static void Points(JsonElement root, List<RoutePoint> points)
    {
        void Add(string ident, string lat, string lon, string airway, string alt)
        {
            ident = ident.ToUpperInvariant();
            if (ident.Length is 0 or > 12 || ident is "TOC" or "TOD") return;
            if (!double.TryParse(lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var la)
                || !double.TryParse(lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lo)
                || Math.Abs(la) > 90 || Math.Abs(lo) > 180) return;
            if (points.Count > 0 && points[^1].Ident == ident && Math.Abs(points[^1].Lat - la) < 0.001) return;
            points.Add(new RoutePoint(ident, la, lo, NavData.IsAirway(airway.ToUpperInvariant()) ? airway.ToUpperInvariant() : "", Int(alt)));
        }
        Add(Str(root, "origin", "icao_code"), Str(root, "origin", "pos_lat"), Str(root, "origin", "pos_long"), "", "");
        if (root.TryGetProperty("navlog", out var navlog))
            foreach (var f in Fixes(navlog))
                Add(Str(f, "ident"), Str(f, "pos_lat"), Str(f, "pos_long"), Str(f, "via_airway"), Str(f, "altitude_feet"));
        Add(Str(root, "destination", "icao_code"), Str(root, "destination", "pos_lat"), Str(root, "destination", "pos_long"), "", "");
    }

    /// <summary>
    /// The navlog comes in three shapes: {"fix": [...]}, {"fix": {...}} for a single point, and, from some OFPs,
    /// the points keyed by index ({"0": {...}, "1": {...}}) or a plain array.
    /// </summary>
    private static IEnumerable<JsonElement> Fixes(JsonElement navlog)
    {
        if (navlog.ValueKind == JsonValueKind.Array) return navlog.EnumerateArray();
        if (navlog.ValueKind != JsonValueKind.Object) return [];
        if (navlog.TryGetProperty("fix", out var fixes))
            return fixes.ValueKind == JsonValueKind.Array ? fixes.EnumerateArray() : [fixes];
        return navlog.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty("ident", out _)).Select(p => p.Value);
    }

    /// <summary>Route points sent back by the form: kept only when they are a well-formed route.</summary>
    public static string Clean(string? json) => StoredRoute.Parse(json)?.ToJson() ?? "";

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

/// <summary>
/// What a flight plan keeps from its SimBrief import, as the JSON in <see cref="FlightPlan.Waypoints"/>:
/// {"points":[[ident, lat, lon, airway, altitude], …], "airline":…, "reg":…, …}. Older rows hold just the points array.
/// </summary>
public sealed class StoredRoute
{
    public List<RoutePoint> Points { get; } = [];
    public string Airline { get; set; } = "";
    public string Flight { get; set; } = "";
    public string Registration { get; set; } = "";
    public string AircraftName { get; set; } = "";
    public string StepClimbs { get; set; } = "";
    public string CruiseMach { get; set; } = "";
    public string Airac { get; set; } = "";
    public int RouteDistance { get; set; }
    public long OffTime { get; set; }
    public long OnTime { get; set; }

    public static StoredRoute? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var root = JsonNode.Parse(json);
            var r = new StoredRoute();
            JsonArray? points = root as JsonArray;
            if (root is JsonObject o)
            {
                points = o["points"] as JsonArray;
                r.Airline = Text(o, "airline", 8);
                r.Flight = Text(o, "flight", 8);
                r.Registration = Text(o, "reg", 12);
                r.AircraftName = Text(o, "type", 24);
                r.StepClimbs = Text(o, "steps", 300);
                r.CruiseMach = Text(o, "mach", 6);
                r.Airac = Text(o, "airac", 6);
                r.RouteDistance = Math.Clamp((int)Number(o, "dist"), 0, 30000);
                r.OffTime = (long)Number(o, "off");
                r.OnTime = (long)Number(o, "on");
            }
            if (points == null) return null;
            foreach (var node in points)
            {
                if (node is not JsonArray p || p.Count is < 3 or > 5) return null;
                string ident = (p[0]?.GetValue<string>() ?? "").ToUpperInvariant();
                double lat = p[1]!.GetValue<double>(), lon = p[2]!.GetValue<double>();
                if (ident.Length is 0 or > 12 || Math.Abs(lat) > 90 || Math.Abs(lon) > 180) return null;
                string airway = p.Count > 3 ? (p[3]?.GetValue<string>() ?? "").ToUpperInvariant() : "";
                int alt = p.Count > 4 ? Math.Clamp((int)(p[4]?.GetValue<double>() ?? 0), 0, 100000) : 0;
                r.Points.Add(new RoutePoint(ident, lat, lon, NavData.IsAirway(airway) ? airway : "", alt));
                if (r.Points.Count > Simbrief.MaxWaypoints) break;
            }
            return r.Points.Count > 1 ? r : null;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or InvalidCastException) { return null; }
    }

    public string ToJson()
    {
        var points = new JsonArray(Points.Select(p => (JsonNode)new JsonArray(p.Ident, Math.Round(p.Lat, 4), Math.Round(p.Lon, 4), p.Airway, p.Altitude)).ToArray());
        var o = new JsonObject { ["points"] = points };
        if (Airline.Length > 0) o["airline"] = Airline;
        if (Flight.Length > 0) o["flight"] = Flight;
        if (Registration.Length > 0) o["reg"] = Registration;
        if (AircraftName.Length > 0) o["type"] = AircraftName;
        if (StepClimbs.Length > 0) o["steps"] = StepClimbs;
        if (CruiseMach.Length > 0) o["mach"] = CruiseMach;
        if (Airac.Length > 0) o["airac"] = Airac;
        if (RouteDistance > 0) o["dist"] = RouteDistance;
        if (OffTime > 0) o["off"] = OffTime;
        if (OnTime > 0) o["on"] = OnTime;
        return o.ToJsonString();
    }

    /// <summary>The extras without the points, for the map card.</summary>
    public JsonObject Extras()
    {
        var o = JsonNode.Parse(ToJson())!.AsObject();
        o.Remove("points");
        return o;
    }

    private static string Text(JsonObject o, string name, int max)
    {
        string s = o[name] is JsonValue v && v.TryGetValue<string>(out var str) ? str.Trim() : "";
        return s.Length > max ? s[..max] : s;
    }

    private static double Number(JsonObject o, string name) =>
        o[name] is JsonValue v && v.TryGetValue<double>(out var d) && double.IsFinite(d) ? d : 0;
}
