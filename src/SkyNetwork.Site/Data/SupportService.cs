using Dapper;

namespace SkyNetwork.Site.Data;

/// <summary>Support tickets and training requests.</summary>
public sealed class SupportService(Database db, AuditService audit)
{
    public static readonly IReadOnlyDictionary<string, string> TicketStatuses = new Dictionary<string, string>
    {
        ["open"] = "open", ["answered"] = "answered", ["closed"] = "closed",
    };

    public static readonly IReadOnlyDictionary<string, string> TrainingStatuses = new Dictionary<string, string>
    {
        ["open"] = "waiting", ["accepted"] = "in progress", ["completed"] = "completed", ["declined"] = "declined",
    };

    public long OpenTicket(long? cid, string email, string subject, string body)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        long now = Database.Now();
        long id = c.ExecuteScalar<long>("""
            INSERT INTO tickets (cid, email, subject, status, created_at, updated_at) VALUES (@cid, @email, @subject, 'open', @now, @now) RETURNING id
            """, new { cid, email, subject, now }, tx);
        c.Execute("INSERT INTO ticket_messages (ticket_id, cid, staff, body, created_at) VALUES (@id, @cid, 0, @body, @now)",
            new { id, cid, body, now }, tx);
        tx.Commit();
        return id;
    }

    public IReadOnlyList<Ticket> Tickets(long? cid = null, string? status = null)
    {
        using var c = db.Open();
        return c.Query<Ticket>("""
            SELECT t.*, COALESCE(m.name, '') AS name FROM tickets t LEFT JOIN members m ON m.cid = t.cid
            WHERE (@cid IS NULL OR t.cid = @cid) AND (@status IS NULL OR t.status = @status)
            ORDER BY t.status = 'closed', t.updated_at DESC LIMIT 300
            """, new { cid, status }).ToList();
    }

    public Ticket? Ticket(long id)
    {
        using var c = db.Open();
        return c.QuerySingleOrDefault<Ticket>("""
            SELECT t.*, COALESCE(m.name, '') AS name FROM tickets t LEFT JOIN members m ON m.cid = t.cid WHERE t.id = @id
            """, new { id });
    }

    public IReadOnlyList<TicketMessage> Messages(long ticketId)
    {
        using var c = db.Open();
        return c.Query<TicketMessage>("""
            SELECT x.*, COALESCE(m.name, '') AS name FROM ticket_messages x LEFT JOIN members m ON m.cid = x.cid
            WHERE x.ticket_id = @ticketId ORDER BY x.id
            """, new { ticketId }).ToList();
    }

    /// <summary>A reply; a staff reply marks the ticket answered, a member reply reopens it.</summary>
    public void Reply(long ticketId, long cid, bool staff, string body)
    {
        using var c = db.Open();
        long now = Database.Now();
        c.Execute("INSERT INTO ticket_messages (ticket_id, cid, staff, body, created_at) VALUES (@ticketId, @cid, @staff, @body, @now)",
            new { ticketId, cid, staff = staff ? 1 : 0, body, now });
        c.Execute("UPDATE tickets SET status = @status, updated_at = @now WHERE id = @ticketId",
            new { ticketId, now, status = staff ? "answered" : "open" });
        if (staff) audit.Log(cid, "ticket", ticketId.ToString(), "reply");
    }

    public void SetTicketStatus(long actor, long ticketId, string status)
    {
        if (!TicketStatuses.ContainsKey(status)) return;
        using var c = db.Open();
        c.Execute("UPDATE tickets SET status = @status, updated_at = @now WHERE id = @ticketId", new { ticketId, status, now = Database.Now() });
        audit.Log(actor, "ticket", ticketId.ToString(), TicketStatuses[status]);
    }

    // ---- training ----

    /// <summary>
    /// Applies to the member's division academy for the next rating on a track; returns an (English)
    /// error or null. The division trains and examines, then sends a rating request.
    /// </summary>
    public string? RequestTraining(long cid, long? divisionId, string track, int currentLevel, int target, string message)
    {
        if (divisionId == null) return "Choose your division first";
        if (!TrainingTracks.Valid(track) || TrainingTracks.Next(track, currentLevel) != target) return "Choose the next rating";
        using var c = db.Open();
        bool pending = c.ExecuteScalar<long>("""
            SELECT COUNT(*) FROM training_requests WHERE cid = @cid AND track = @track AND status IN ('open', 'accepted')
            """, new { cid, track }) > 0;
        if (pending) return "You already have an active request for this rating";
        long now = Database.Now();
        c.Execute("""
            INSERT INTO training_requests (cid, division_id, track, target_rating, message, status, created_at, updated_at)
            VALUES (@cid, @divisionId, @track, @target, @message, 'open', @now, @now)
            """, new { cid, divisionId, track, target, message, now });
        return null;
    }

    // "rating" is the member's current level on the request's track.
    private const string TrainingSelect = """
        SELECT r.*, COALESCE(m.name, '') AS name, COALESCE(d.code, '') AS division_code,
               CASE r.track WHEN 'pilot' THEN COALESCE(p.pilot_rating, 0)
                            WHEN 'military' THEN COALESCE(p.military_rating, 0)
                            ELSE COALESCE(m.rating, 1) END AS rating
        FROM training_requests r LEFT JOIN members m ON m.cid = r.cid LEFT JOIN member_profiles p ON p.cid = r.cid
        LEFT JOIN divisions d ON d.id = r.division_id
        """;

    public IReadOnlyList<TrainingRequest> Training(long? cid = null, bool activeOnly = false, long? divisionId = null)
    {
        using var c = db.Open();
        return c.Query<TrainingRequest>(TrainingSelect + """
             WHERE (@cid IS NULL OR r.cid = @cid) AND (NOT @active OR r.status IN ('open', 'accepted'))
               AND (@divisionId IS NULL OR r.division_id = @divisionId)
            ORDER BY r.status NOT IN ('open', 'accepted'), r.created_at DESC LIMIT 300
            """, new { cid, active = activeOnly, divisionId }).ToList();
    }

    public TrainingRequest? TrainingRequest(long id)
    {
        using var c = db.Open();
        return c.QuerySingleOrDefault<TrainingRequest>(TrainingSelect + " WHERE r.id = @id", new { id });
    }

    /// <param name="actor">Staff member on the site, or 0 for a division academy (through its API).</param>
    /// <param name="instructor">The instructor in charge; the actor when not given.</param>
    public void UpdateTraining(long actor, long id, string status, string comment, long? instructor = null)
    {
        if (!TrainingStatuses.ContainsKey(status)) return;
        using var c = db.Open();
        c.Execute("""
            UPDATE training_requests SET status = @status, staff_comment = @comment,
                   instructor_cid = COALESCE(@instructor, instructor_cid), updated_at = @now WHERE id = @id
            """, new { id, status, comment, instructor = instructor ?? (actor > 0 ? actor : null), now = Database.Now() });
        audit.Log(actor, "training", id.ToString(), TrainingStatuses[status]);
    }

    public (int OpenTickets, int OpenTraining) Counts()
    {
        using var c = db.Open();
        return (c.ExecuteScalar<int>("SELECT COUNT(*) FROM tickets WHERE status = 'open'"),
                c.ExecuteScalar<int>("SELECT COUNT(*) FROM training_requests WHERE status = 'open'"));
    }
}
