using Dapper;

namespace SkyNetwork.Site.Data;

/// <summary>Support tickets.</summary>
public sealed class SupportService(Database db, AuditService audit)
{
    public static readonly IReadOnlyDictionary<string, string> TicketStatuses = new Dictionary<string, string>
    {
        ["open"] = "open", ["answered"] = "answered", ["closed"] = "closed",
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

    public int OpenTickets()
    {
        using var c = db.Open();
        return c.ExecuteScalar<int>("SELECT COUNT(*) FROM tickets WHERE status = 'open'");
    }
}
