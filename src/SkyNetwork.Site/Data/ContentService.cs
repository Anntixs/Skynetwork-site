using Dapper;

namespace SkyNetwork.Site.Data;

/// <summary>Events, news and ATC bookings.</summary>
public sealed class ContentService(Database db, AuditService audit)
{
    // ---- events ----

    public IReadOnlyList<NetworkEvent> UpcomingEvents(int limit = 20, bool includeUnpublished = false)
    {
        using var c = db.Open();
        return c.Query<NetworkEvent>("""
            SELECT * FROM events WHERE ends_at >= @now AND (published = 1 OR @all) ORDER BY starts_at LIMIT @limit
            """, new { now = Database.Now(), limit, all = includeUnpublished }).ToList();
    }

    public IReadOnlyList<NetworkEvent> AllEvents()
    {
        using var c = db.Open();
        return c.Query<NetworkEvent>("SELECT * FROM events ORDER BY starts_at DESC").ToList();
    }

    public NetworkEvent? Event(long id)
    {
        using var c = db.Open();
        return c.QuerySingleOrDefault<NetworkEvent>("SELECT * FROM events WHERE id = @id", new { id });
    }

    public long SaveEvent(long actor, NetworkEvent e)
    {
        using var c = db.Open();
        var args = new { e.Id, e.Title, e.Summary, e.Body, e.Airports, e.StartsAt, e.EndsAt, e.Banner, e.BannerSize, e.BannerFocus, published = e.Published ? 1 : 0, actor, now = Database.Now() };
        long id = e.Id;
        if (e.Id == 0)
            id = c.ExecuteScalar<long>("""
                INSERT INTO events (title, summary, body, airports, starts_at, ends_at, banner, banner_size, banner_focus, published, created_by, created_at)
                VALUES (@Title, @Summary, @Body, @Airports, @StartsAt, @EndsAt, @Banner, @BannerSize, @BannerFocus, @published, @actor, @now) RETURNING id
                """, args);
        else
            c.Execute("""
                UPDATE events SET title=@Title, summary=@Summary, body=@Body, airports=@Airports, starts_at=@StartsAt,
                    ends_at=@EndsAt, banner=@Banner, banner_size=@BannerSize, banner_focus=@BannerFocus, published=@published WHERE id=@Id
                """, args);
        audit.Log(actor, "event", id.ToString(), e.Title);
        return id;
    }

    public void DeleteEvent(long actor, long id)
    {
        using var c = db.Open();
        var title = c.ExecuteScalar<string>("SELECT title FROM events WHERE id = @id", new { id }) ?? "";
        c.Execute("DELETE FROM events WHERE id = @id", new { id });
        audit.Log(actor, "event-delete", id.ToString(), title);
    }

    // ---- news ----

    public IReadOnlyList<NewsPost> News(int limit = 20, bool includeUnpublished = false)
    {
        using var c = db.Open();
        return c.Query<NewsPost>("""
            SELECT n.*, COALESCE(m.name, '') AS author_name FROM news n LEFT JOIN members m ON m.cid = n.author_cid
            WHERE n.published = 1 OR @all ORDER BY n.created_at DESC, n.id DESC LIMIT @limit
            """, new { limit, all = includeUnpublished }).ToList();
    }

    public NewsPost? Post(long id)
    {
        using var c = db.Open();
        return c.QuerySingleOrDefault<NewsPost>("""
            SELECT n.*, COALESCE(m.name, '') AS author_name FROM news n LEFT JOIN members m ON m.cid = n.author_cid WHERE n.id = @id
            """, new { id });
    }

    public long SavePost(long actor, NewsPost p)
    {
        using var c = db.Open();
        var args = new { p.Id, p.Title, p.Body, p.Banner, p.BannerSize, p.BannerFocus, published = p.Published ? 1 : 0, actor, now = Database.Now() };
        long id = p.Id;
        if (p.Id == 0)
            id = c.ExecuteScalar<long>("""
                INSERT INTO news (title, body, banner, banner_size, banner_focus, published, author_cid, created_at)
                VALUES (@Title, @Body, @Banner, @BannerSize, @BannerFocus, @published, @actor, @now) RETURNING id
                """, args);
        else
            c.Execute("""
                UPDATE news SET title=@Title, body=@Body, banner=@Banner, banner_size=@BannerSize, banner_focus=@BannerFocus,
                    published=@published WHERE id=@Id
                """, args);
        audit.Log(actor, "news", id.ToString(), p.Title);
        return id;
    }

    public void DeletePost(long actor, long id)
    {
        using var c = db.Open();
        var title = c.ExecuteScalar<string>("SELECT title FROM news WHERE id = @id", new { id }) ?? "";
        c.Execute("DELETE FROM news WHERE id = @id", new { id });
        audit.Log(actor, "news-delete", id.ToString(), title);
    }

    // ---- ATC bookings ----

    public IReadOnlyList<Booking> Bookings(long? cid = null, int limit = 200)
    {
        using var c = db.Open();
        return c.Query<Booking>("""
            SELECT b.*, COALESCE(m.name, '') AS name, COALESCE(m.rating, 1) AS rating
            FROM bookings b LEFT JOIN members m ON m.cid = b.cid
            WHERE b.ends_at >= @now AND (@cid IS NULL OR b.cid = @cid)
            ORDER BY b.starts_at LIMIT @limit
            """, new { now = Database.Now(), cid, limit }).ToList();
    }

    /// <summary>Books a position; returns an English error (a format string with {0} = callsign) or null.</summary>
    public string? Book(long cid, string callsign, long start, long end)
    {
        if (end <= start) return "The end must be after the start";
        if (end - start > 12 * 3600) return "At most 12 hours";
        if (start < Database.Now() - 600) return "This time has already passed";
        using var c = db.Open();
        bool overlap = c.ExecuteScalar<long>("""
            SELECT COUNT(*) FROM bookings WHERE callsign = @callsign COLLATE NOCASE AND starts_at < @end AND ends_at > @start
            """, new { callsign, start, end }) > 0;
        if (overlap) return "{0} is already booked for this time";
        c.Execute("INSERT INTO bookings (cid, callsign, starts_at, ends_at, created_at) VALUES (@cid, @callsign, @start, @end, @now)",
            new { cid, callsign, start, end, now = Database.Now() });
        return null;
    }

    public Booking? BookingById(long id)
    {
        using var c = db.Open();
        return c.QuerySingleOrDefault<Booking>("SELECT b.*, '' AS name, 1 AS rating FROM bookings b WHERE id = @id", new { id });
    }

    public void DeleteBooking(long id, long? staffActor = null)
    {
        using var c = db.Open();
        var b = c.QuerySingleOrDefault<Booking>("SELECT b.*, '' AS name, 1 AS rating FROM bookings b WHERE id = @id", new { id });
        c.Execute("DELETE FROM bookings WHERE id = @id", new { id });
        if (staffActor is { } actor && b != null) audit.Log(actor, "booking-delete", b.Cid.ToString(), $"{b.Callsign} {b.Start:dd.MM HH:mm}z");
    }
}
