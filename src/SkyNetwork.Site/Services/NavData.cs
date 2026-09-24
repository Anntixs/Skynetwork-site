using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using SkyNetwork.Site.Data;

namespace SkyNetwork.Site.Services;

/// <summary>A point of a drawn route: a waypoint, navaid, airport or coordinate, with the airway that led to it ("" for direct).</summary>
public readonly record struct RoutePoint(string Ident, double Lat, double Lon, string Airway, int Altitude);

/// <summary>
/// Waypoints and airways for drawing the routes of flight plans filed without SimBrief. Two sources: a bundled world
/// set (Nav/fixes.dat.gz and Nav/airways.dat.gz, see Nav/LICENSE.txt) and everything seen in the SimBrief plans that
/// members import, kept in the site's database — so the data gets fresher the more the network is used.
/// </summary>
public sealed partial class NavData(Database db, IWebHostEnvironment env, ILogger<NavData> log)
{
    /// <summary>A fix further than this from the previous point is another fix with the same name somewhere else.</summary>
    private const double MaxLegNm = 1500;
    /// <summary>Airway segment ends this close together are the same fix.</summary>
    private const double SameFixNm = 2;

    [GeneratedRegex(@"^[A-Z]{1,2}\d{1,4}[A-Z]?$")] private static partial Regex AirwayName();
    [GeneratedRegex(@"^[A-Z]{3,5}\d[A-Z]{0,2}$")] private static partial Regex SidStar();
    [GeneratedRegex(@"^[NMK]\d{3,4}[FSAM]\d{3,4}$")] private static partial Regex SpeedLevel();
    [GeneratedRegex(@"^(\d{2})(\d{2})?([NS])(\d{3})(\d{2})?([EW])$")] private static partial Regex Coordinate();
    [GeneratedRegex(@"^[A-Z0-9]{1,6}$")] private static partial Regex FixName();

    public static bool IsAirway(string s) => AirwayName().IsMatch(s);

    private sealed record Segment(string A, double ALat, double ALon, string B, double BLat, double BLon);

    private sealed record BundledData(
        Dictionary<string, List<(double Lat, double Lon)>> Fixes,
        Dictionary<string, List<Segment>> Airways,
        Dictionary<string, (double Lat, double Lon)> Airports);

    // The bundled files are read once per process, whichever site instance asks first (tests create several).
    private static readonly object BundledLock = new();
    private static BundledData? _bundledCache;
    private static string? _bundledRoot;

    private readonly object _lock = new();
    private readonly Dictionary<string, List<(double Lat, double Lon)>> _learnedFixes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Segment>> _learnedAirways = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AirwayGraph?> _graphs = new(StringComparer.Ordinal);
    private bool _learnedLoaded;

    public (int Fixes, int Airways, int LearnedFixes, int LearnedAirways) Counts
    {
        get
        {
            var b = Bundled;
            lock (_lock) { EnsureLearned(); return (b.Fixes.Count, b.Airways.Count, _learnedFixes.Count, _learnedAirways.Count); }
        }
    }

    /// <summary>
    /// The route as points: airports, fixes, navaids and coordinates in order, airways expanded fix by fix. Tokens that
    /// could not be placed are returned in <c>Unresolved</c>; the line simply goes straight across them.
    /// </summary>
    public (List<RoutePoint> Points, List<string> Unresolved) Decode(string departure, string destination, string route)
    {
        var b = Bundled;
        var points = new List<RoutePoint>();
        var unresolved = new List<string>();
        departure = departure.ToUpperInvariant();
        destination = destination.ToUpperInvariant();
        (double Lat, double Lon)? prev = null, dest = null;
        if (b.Airports.TryGetValue(departure, out var dep)) { prev = dep; points.Add(new RoutePoint(departure, dep.Lat, dep.Lon, "", 0)); }
        if (b.Airports.TryGetValue(destination, out var arr)) dest = arr;

        var tokens = route.ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Split('/')[0]).Where(t => t.Length > 0).ToList();
        lock (_lock)
        {
            EnsureLearned();
            for (int i = 0; i < tokens.Count; i++)
            {
                string tok = tokens[i];
                if (tok == departure || tok == destination || tok is "DCT" or "DIRECT" or "SID" or "STAR" or "IFR" or "VFR") continue;
                if (SpeedLevel().IsMatch(tok)) continue;
                if (TryCoordinate(tok, out var pos))
                {
                    points.Add(new RoutePoint(tok, pos.Lat, pos.Lon, "", 0));
                    prev = pos;
                    continue;
                }
                // An airway between the previous fix and the next token.
                if (points.Count > 0 && i + 1 < tokens.Count && HasAirway(tok))
                {
                    var along = Expand(tok, points[^1], tokens[i + 1]);
                    if (along != null)
                    {
                        points.AddRange(along);
                        prev = (points[^1].Lat, points[^1].Lon);
                        i++;
                        continue;
                    }
                }
                if (SidStar().IsMatch(tok) && !HasFix(tok)) continue;   // procedure names: the next token is its fix
                var candidates = Candidates(tok);
                if (candidates.Count == 0)
                {
                    if (!IsAirway(tok)) unresolved.Add(tok);
                    continue;
                }
                var reference = prev ?? dest;
                var best = reference is { } r ? candidates.MinBy(c => Distance(r.Lat, r.Lon, c.Lat, c.Lon)) : candidates[0];
                if (reference is { } rr && Distance(rr.Lat, rr.Lon, best.Lat, best.Lon) > MaxLegNm) { unresolved.Add(tok); continue; }
                points.Add(new RoutePoint(tok, best.Lat, best.Lon, "", 0));
                prev = best;
            }
        }
        if (dest is { } d && (points.Count == 0 || points[^1].Ident != destination)) points.Add(new RoutePoint(destination, d.Lat, d.Lon, "", 0));
        return (points, unresolved);
    }

    /// <summary>Keeps the fixes and airway segments of an imported SimBrief route for later routes.</summary>
    public void Learn(IReadOnlyList<RoutePoint> route, string departure, string destination)
    {
        var newFixes = new List<RoutePoint>();
        var newSegments = new List<(string Name, RoutePoint A, RoutePoint B)>();
        lock (_lock)
        {
            EnsureLearned();
            for (int i = 0; i < route.Count; i++)
            {
                var p = route[i];
                string ident = p.Ident.ToUpperInvariant();
                if (ident == departure.ToUpperInvariant() || ident == destination.ToUpperInvariant() || ident is "TOC" or "TOD") continue;
                if (!FixName().IsMatch(ident) || Coordinate().IsMatch(ident) || Math.Abs(p.Lat) > 90 || Math.Abs(p.Lon) > 180) continue;
                if (!_learnedFixes.TryGetValue(ident, out var known)) _learnedFixes[ident] = known = [];
                if (!known.Any(k => Distance(k.Lat, k.Lon, p.Lat, p.Lon) < 1))
                {
                    known.Add((p.Lat, p.Lon));
                    newFixes.Add(p with { Ident = ident });
                }
                if (i > 0 && IsAirway(p.Airway) && route[i - 1].Ident != departure)
                {
                    var a = route[i - 1];
                    if (!_learnedAirways.TryGetValue(p.Airway, out var segs)) _learnedAirways[p.Airway] = segs = [];
                    if (!segs.Any(s => s.A == a.Ident && s.B == ident || s.A == ident && s.B == a.Ident))
                    {
                        segs.Add(new Segment(a.Ident, a.Lat, a.Lon, ident, p.Lat, p.Lon));
                        newSegments.Add((p.Airway, a, p with { Ident = ident }));
                        _graphs.Remove(p.Airway);
                    }
                }
            }
        }
        if (newFixes.Count == 0 && newSegments.Count == 0) return;
        try
        {
            using var c = db.Open();
            using var tx = c.BeginTransaction();
            long now = Database.Now();
            foreach (var f in newFixes)
                c.Execute("INSERT OR IGNORE INTO nav_fixes (ident, lat, lon, seen_at) VALUES (@Ident, @Lat, @Lon, @now)",
                    new { f.Ident, Lat = Math.Round(f.Lat, 4), Lon = Math.Round(f.Lon, 4), now }, tx);
            foreach (var (name, a, bb) in newSegments)
                c.Execute("""
                    INSERT OR IGNORE INTO nav_airways (name, a, a_lat, a_lon, b, b_lat, b_lon, seen_at)
                    VALUES (@name, @a, @aLat, @aLon, @b, @bLat, @bLon, @now)
                    """, new { name, a = a.Ident, aLat = Math.Round(a.Lat, 4), aLon = Math.Round(a.Lon, 4), b = bb.Ident, bLat = Math.Round(bb.Lat, 4), bLon = Math.Round(bb.Lon, 4), now }, tx);
            tx.Commit();
            log.LogInformation("Learned {Fixes} fixes and {Segments} airway segments from a SimBrief route", newFixes.Count, newSegments.Count);
        }
        catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            log.LogWarning("Nav data not saved: {Error}", e.Message);
        }
    }

    // ---- lookups ----

    private BundledData Bundled
    {
        get
        {
            lock (BundledLock)
            {
                if (_bundledCache == null || _bundledRoot != env.ContentRootPath)
                {
                    _bundledCache = Load(env.ContentRootPath, log);
                    _bundledRoot = env.ContentRootPath;
                }
                return _bundledCache;
            }
        }
    }

    private void EnsureLearned()
    {
        if (_learnedLoaded) return;
        _learnedLoaded = true;
        try
        {
            using var c = db.Open();
            foreach (var f in c.Query<(string Ident, double Lat, double Lon)>("SELECT ident, lat, lon FROM nav_fixes"))
            {
                if (!_learnedFixes.TryGetValue(f.Ident, out var list)) _learnedFixes[f.Ident] = list = [];
                list.Add((f.Lat, f.Lon));
            }
            foreach (var s in c.Query<(string Name, string A, double ALat, double ALon, string B, double BLat, double BLon)>(
                         "SELECT name, a, a_lat, a_lon, b, b_lat, b_lon FROM nav_airways"))
            {
                if (!_learnedAirways.TryGetValue(s.Name, out var list)) _learnedAirways[s.Name] = list = [];
                list.Add(new Segment(s.A, s.ALat, s.ALon, s.B, s.BLat, s.BLon));
            }
        }
        catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            log.LogWarning("Nav data not loaded: {Error}", e.Message);
        }
    }

    private bool HasAirway(string name) => _learnedAirways.ContainsKey(name) || Bundled.Airways.ContainsKey(name);
    private bool HasFix(string ident) => _learnedFixes.ContainsKey(ident) || Bundled.Fixes.ContainsKey(ident);

    // Learned positions first: they are the current ones.
    private List<(double Lat, double Lon)> Candidates(string ident)
    {
        var list = new List<(double, double)>();
        if (_learnedFixes.TryGetValue(ident, out var learned)) list.AddRange(learned);
        if (Bundled.Fixes.TryGetValue(ident, out var bundled)) list.AddRange(bundled);
        if (ident.Length == 4 && Bundled.Airports.TryGetValue(ident, out var apt)) list.Add(apt);
        return list;
    }

    private static bool TryCoordinate(string t, out (double Lat, double Lon) pos)
    {
        pos = default;
        var m = Coordinate().Match(t);
        if (!m.Success) return false;
        double lat = int.Parse(m.Groups[1].Value) + (m.Groups[2].Success ? int.Parse(m.Groups[2].Value) / 60.0 : 0);
        double lon = int.Parse(m.Groups[4].Value) + (m.Groups[5].Success ? int.Parse(m.Groups[5].Value) / 60.0 : 0);
        if (lat > 90 || lon > 180) return false;
        pos = (m.Groups[3].Value == "S" ? -lat : lat, m.Groups[6].Value == "W" ? -lon : lon);
        return true;
    }

    // ---- airways ----

    private sealed class AirwayGraph
    {
        public sealed class Node(string ident, double lat, double lon)
        {
            public readonly string Ident = ident;
            public readonly double Lat = lat, Lon = lon;
            public readonly List<Node> Links = [];
        }

        public readonly Dictionary<string, List<Node>> ByIdent = new(StringComparer.Ordinal);

        private Node NodeFor(string ident, double lat, double lon)
        {
            if (!ByIdent.TryGetValue(ident, out var list)) ByIdent[ident] = list = [];
            foreach (var n in list) if (Distance(n.Lat, n.Lon, lat, lon) < SameFixNm) return n;
            var node = new Node(ident, lat, lon);
            list.Add(node);
            return node;
        }

        public void Add(Segment s)
        {
            var a = NodeFor(s.A, s.ALat, s.ALon);
            var b = NodeFor(s.B, s.BLat, s.BLon);
            if (a == b) return;
            if (!a.Links.Contains(b)) a.Links.Add(b);
            if (!b.Links.Contains(a)) b.Links.Add(a);
        }
    }

    private AirwayGraph? Graph(string name)
    {
        if (_graphs.TryGetValue(name, out var cached)) return cached;
        AirwayGraph? g = null;
        foreach (var source in new[] { _learnedAirways, Bundled.Airways })
            if (source.TryGetValue(name, out var segs))
            {
                g ??= new AirwayGraph();
                foreach (var s in segs) g.Add(s);
            }
        return _graphs[name] = g;
    }

    /// <summary>The fixes along an airway after the entry fix up to and including the exit fix; null when the airway does not join them.</summary>
    private List<RoutePoint>? Expand(string airway, RoutePoint from, string exit)
    {
        var g = Graph(airway);
        if (g == null || !g.ByIdent.TryGetValue(from.Ident, out var entries) || !g.ByIdent.ContainsKey(exit)) return null;
        var start = entries.MinBy(n => Distance(n.Lat, n.Lon, from.Lat, from.Lon))!;
        var previous = new Dictionary<AirwayGraph.Node, AirwayGraph.Node?> { [start] = null };
        var queue = new Queue<AirwayGraph.Node>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            if (n.Ident == exit && n != start)
            {
                var path = new List<RoutePoint>();
                for (var at = n; at != null && at != start; at = previous[at]) path.Add(new RoutePoint(at.Ident, at.Lat, at.Lon, airway, 0));
                path.Reverse();
                return path;
            }
            if (previous.Count > 2000) break;
            foreach (var next in n.Links)
                if (!previous.ContainsKey(next)) { previous[next] = n; queue.Enqueue(next); }
        }
        return null;
    }

    // ---- bundled files ----

    private static BundledData Load(string root, ILogger log)
    {
        var fixes = new Dictionary<string, List<(double, double)>>(StringComparer.Ordinal);
        var airways = new Dictionary<string, List<Segment>>(StringComparer.Ordinal);
        var airports = new Dictionary<string, (double, double)>(StringComparer.Ordinal);
        var inv = CultureInfo.InvariantCulture;
        try
        {
            foreach (var line in Lines(Path.Combine(root, "Nav", "fixes.dat.gz")))
            {
                var f = line.Split(' ');
                if (f.Length != 3) continue;
                if (!fixes.TryGetValue(f[0], out var list)) fixes[f[0]] = list = [];
                list.Add((double.Parse(f[1], inv), double.Parse(f[2], inv)));
            }
            foreach (var line in Lines(Path.Combine(root, "Nav", "airways.dat.gz")))
            {
                var f = line.Split(' ');
                if (f.Length != 7) continue;
                if (!airways.TryGetValue(f[0], out var list)) airways[f[0]] = list = [];
                list.Add(new Segment(f[1], double.Parse(f[2], inv), double.Parse(f[3], inv), f[4], double.Parse(f[5], inv), double.Parse(f[6], inv)));
            }
            // VOR and NDB (OurAirports): the same file the map uses.
            string navaids = Path.Combine(root, "wwwroot", "data", "navaids.json");
            if (File.Exists(navaids))
                using (var doc = JsonDocument.Parse(File.ReadAllText(navaids)))
                    foreach (var p in doc.RootElement.EnumerateObject())
                    {
                        if (!fixes.TryGetValue(p.Name, out var list)) fixes[p.Name] = list = [];
                        foreach (var pos in p.Value.EnumerateArray()) list.Add((pos[0].GetDouble(), pos[1].GetDouble()));
                    }
            string apts = Path.Combine(root, "wwwroot", "data", "airports.json");
            if (File.Exists(apts))
                using (var doc = JsonDocument.Parse(File.ReadAllText(apts)))
                    foreach (var p in doc.RootElement.EnumerateObject())
                        airports[p.Name] = (p.Value[0].GetDouble(), p.Value[1].GetDouble());
        }
        catch (Exception e) when (e is IOException or JsonException or FormatException)
        {
            log.LogWarning("Nav data files: {Error}", e.Message);
        }
        log.LogInformation("Nav data: {Fixes} fixes, {Airways} airways, {Airports} airports", fixes.Count, airways.Count, airports.Count);
        return new BundledData(fixes, airways, airports);
    }

    private static IEnumerable<string> Lines(string gzipPath)
    {
        if (!File.Exists(gzipPath)) yield break;
        using var file = File.OpenRead(gzipPath);
        using var gz = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gz);
        while (reader.ReadLine() is { } line) yield return line;
    }

    private const double Rad = Math.PI / 180;

    /// <summary>Nautical miles.</summary>
    public static double Distance(double lat1, double lon1, double lat2, double lon2)
    {
        double dLat = (lat2 - lat1) * Rad, dLon = (lon2 - lon1) * Rad;
        double a = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(lat1 * Rad) * Math.Cos(lat2 * Rad) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * 3440.065 * Math.Asin(Math.Sqrt(Math.Min(1, a)));
    }
}
