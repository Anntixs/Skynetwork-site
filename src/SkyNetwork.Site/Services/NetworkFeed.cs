using Dapper;
using Microsoft.Extensions.Options;
using SkyNetwork.Site.Data;

namespace SkyNetwork.Site.Services;

/// <summary>
/// Polls the FSD data feed, keeps the latest snapshot for the map, lists and API, and writes
/// connection sessions (who was online, as what, how long) for member statistics.
/// </summary>
public sealed class NetworkFeed(IOptions<SiteOptions> options, Database db, IHttpClientFactory http, ILogger<NetworkFeed> log) : BackgroundService
{
    private readonly Dictionary<string, long> _open = [];
    private volatile OnlineSnapshot _current = OnlineSnapshot.Empty;
    private bool _adopted;
    // Where each online aircraft has been since it connected (in memory only), for the flown track on the map.
    private const int MaxTrackPoints = 5000;
    private readonly Dictionary<string, (long Cid, List<TrackPoint> Points)> _tracks = new(StringComparer.OrdinalIgnoreCase);

    public OnlineSnapshot Current => _current;

    public IReadOnlyList<TrackPoint> Track(string callsign)
    {
        lock (_tracks) return _tracks.TryGetValue(callsign, out var t) ? t.Points.ToArray() : [];
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var url = options.Value.DataFeedUrl;
        if (string.IsNullOrWhiteSpace(url)) return;
        var client = http.CreateClient("feed");
        var period = TimeSpan.FromSeconds(Math.Max(5, options.Value.FeedPollSeconds));
        while (!stop.IsCancellationRequested)
        {
            try
            {
                Ingest(await client.GetStringAsync(url, stop));
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or KeyNotFoundException)
            {
                if (stop.IsCancellationRequested) break;
                if (_current.Available) log.LogWarning("Data feed unavailable: {Message}", e.Message);
                _current = _current with { Available = false };
            }
            try { await Task.Delay(period, stop); }
            catch (TaskCanceledException) { break; }
        }
    }

    /// <summary>Takes one feed document (also used by tests).</summary>
    public void Ingest(string json)
    {
        var snapshot = FeedParser.Parse(json, _current);
        _current = snapshot;
        RecordTracks(snapshot);
        TrackSessions(snapshot);
    }

    private void RecordTracks(OnlineSnapshot s)
    {
        long now = Database.Now();
        lock (_tracks)
        {
            var online = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in s.Pilots)
            {
                if (p.Latitude is not { } lat || p.Longitude is not { } lon) continue;
                online.Add(p.Callsign);
                // A callsign taken by someone else starts a new track.
                if (!_tracks.TryGetValue(p.Callsign, out var t) || t.Cid != p.Cid) _tracks[p.Callsign] = t = (p.Cid, []);
                if (t.Points.Count > 0)
                {
                    var last = t.Points[^1];
                    // A point when the aircraft has moved, climbed or descended, or every 5 minutes while it stands still.
                    if (FeedParser.Distance(last.Latitude, last.Longitude, lat, lon) < 0.3
                        && Math.Abs(last.Altitude - p.Altitude) < 500 && now - last.Time < 300) continue;
                }
                t.Points.Add(new TrackPoint(lat, lon, p.Altitude, p.Groundspeed, now));
                if (t.Points.Count > MaxTrackPoints) t.Points.RemoveRange(0, t.Points.Count - MaxTrackPoints);
            }
            foreach (var gone in _tracks.Keys.Where(k => !online.Contains(k)).ToList()) _tracks.Remove(gone);
        }
    }

    private static string Key(string kind, long cid, string callsign) => $"{kind}:{cid}:{callsign.ToUpperInvariant()}";

    private void TrackSessions(OnlineSnapshot s)
    {
        using var c = db.Open();
        long now = Database.Now();
        var online = new Dictionary<string, (long Cid, string Callsign, string Kind, string Details, DateTime Logon)>();
        foreach (var p in s.Pilots)
            online[Key("pilot", p.Cid, p.Callsign)] = (p.Cid, p.Callsign, "pilot",
                p.FlightPlan is { } fp ? $"{fp.Aircraft} {fp.Departure}→{fp.Destination}" : "", p.LogonTime);
        foreach (var a in s.Controllers)
            online[Key("atc", a.Cid, a.Callsign)] = (a.Cid, a.Callsign, "atc", $"{a.Frequency} {a.Rating}", a.LogonTime);

        if (!_adopted)
        {
            // After a restart: continue the sessions of people still online, close the rest.
            foreach (var open in c.Query<NetworkSession>("SELECT * FROM network_sessions WHERE ended_at IS NULL"))
            {
                var key = Key(open.Kind, open.Cid, open.Callsign);
                if (online.ContainsKey(key) && !_open.ContainsKey(key)) _open[key] = open.Id;
                else c.Execute("UPDATE network_sessions SET ended_at = @now WHERE id = @id", new { id = open.Id, now });
            }
            _adopted = true;
        }

        foreach (var (key, x) in online)
        {
            if (_open.ContainsKey(key)) continue;
            long started = Math.Min(now, new DateTimeOffset(x.Logon).ToUnixTimeSeconds());
            _open[key] = c.ExecuteScalar<long>("""
                INSERT INTO network_sessions (cid, callsign, kind, details, started_at) VALUES (@Cid, @Callsign, @Kind, @Details, @started) RETURNING id
                """, new { x.Cid, x.Callsign, x.Kind, x.Details, started = started > 0 ? started : now });
        }
        foreach (var (key, id) in _open.Where(kv => !online.ContainsKey(kv.Key)).ToList())
        {
            c.Execute("UPDATE network_sessions SET ended_at = @now WHERE id = @id", new { id, now });
            _open.Remove(key);
        }
    }
}
