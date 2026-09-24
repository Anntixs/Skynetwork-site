using Dapper;

namespace SkyNetwork.Site.Data;

public sealed record MemberHours(double PilotHours, double AtcHours, int PilotSessions, int AtcSessions);

public sealed class TopEntry
{
    public long Cid { get; set; }
    public string Name { get; set; } = "";
    public double Hours { get; set; }
}

/// <summary>Statistics from the connection log written by the data feed tracker.</summary>
public sealed class SessionService(Database db)
{
    private const string Seconds = "(COALESCE(ended_at, CAST(strftime('%s','now') AS INTEGER)) - started_at)";

    public MemberHours Hours(long cid)
    {
        using var c = db.Open();
        var rows = c.Query<(string Kind, double Seconds, long Count)>(
            $"SELECT kind, SUM({Seconds}) * 1.0, COUNT(*) FROM network_sessions WHERE cid = @cid GROUP BY kind", new { cid }).ToList();
        double H(string k) => rows.Where(r => r.Kind == k).Sum(r => r.Seconds) / 3600;
        int N(string k) => (int)rows.Where(r => r.Kind == k).Sum(r => r.Count);
        return new MemberHours(H("pilot"), H("atc"), N("pilot"), N("atc"));
    }

    public IReadOnlyList<NetworkSession> Recent(long cid, int limit = 25)
    {
        using var c = db.Open();
        return c.Query<NetworkSession>("SELECT * FROM network_sessions WHERE cid = @cid ORDER BY started_at DESC LIMIT @limit",
            new { cid, limit }).ToList();
    }

    /// <summary>Members with the most hours in the last <paramref name="days"/> days.</summary>
    public IReadOnlyList<TopEntry> Top(string kind, int days = 30, int limit = 10)
    {
        using var c = db.Open();
        long since = Database.Now() - days * 86400L;
        return c.Query<TopEntry>($"""
            SELECT s.cid, COALESCE(m.name, '') AS name, SUM({Seconds}) / 3600.0 AS hours
            FROM network_sessions s LEFT JOIN members m ON m.cid = s.cid
            WHERE s.kind = @kind AND s.started_at >= @since GROUP BY s.cid ORDER BY hours DESC LIMIT @limit
            """, new { kind, since, limit }).ToList();
    }

    public (int Today, int Month) SessionCounts()
    {
        using var c = db.Open();
        long now = Database.Now();
        long day = now - now % 86400;
        return (c.ExecuteScalar<int>("SELECT COUNT(*) FROM network_sessions WHERE started_at >= @day", new { day }),
                c.ExecuteScalar<int>("SELECT COUNT(*) FROM network_sessions WHERE started_at >= @since", new { since = now - 30 * 86400L }));
    }
}
