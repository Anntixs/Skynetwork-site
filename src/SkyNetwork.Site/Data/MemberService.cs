using Dapper;
using Microsoft.Extensions.Options;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Data;

public sealed class MemberService(Database db, IOptions<SiteOptions> options, AuditService audit)
{
    private const string Select = """
        SELECT m.cid, m.name, m.rating, m.suspended, p.email, COALESCE(p.country, '') AS country,
               p.registered_at, p.last_login_at, COALESCE(p.suspension_reason, '') AS suspension_reason,
               p.suspended_until, COALESCE(p.pilot_rating, 0) AS pilot_rating, COALESCE(p.military_rating, 0) AS military_rating
        FROM members m LEFT JOIN member_profiles p ON p.cid = m.cid
        """;

    public Member? Find(long cid)
    {
        using var c = db.Open();
        return c.QuerySingleOrDefault<Member>(Select + " WHERE m.cid = @cid", new { cid });
    }

    public bool EmailTaken(string email)
    {
        using var c = db.Open();
        return c.ExecuteScalar<long>("SELECT COUNT(*) FROM member_profiles WHERE email = @email COLLATE NOCASE", new { email }) > 0;
    }

    /// <summary>Creates a member with the next free CID and returns it.</summary>
    public long Register(string name, string email, string country, string password)
    {
        var (salt, hash) = PasswordHasher.Hash(password);
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        long cid = Math.Max(options.Value.FirstCid, c.ExecuteScalar<long?>("SELECT MAX(cid) FROM members", transaction: tx) + 1 ?? 0);
        c.Execute("INSERT INTO members (cid, name, rating, salt, hash) VALUES (@cid, @name, 1, @salt, @hash)",
            new { cid, name, salt, hash }, tx);
        c.Execute("INSERT INTO member_profiles (cid, email, country, registered_at) VALUES (@cid, @email, @country, @now)",
            new { cid, email, country, now = Database.Now() }, tx);
        tx.Commit();
        return cid;
    }

    /// <summary>Checks CID and password like the FSD server does; suspended members are refused.</summary>
    public Member? Authenticate(long cid, string password)
    {
        using var c = db.Open();
        var row = c.QuerySingleOrDefault<Credentials>("SELECT salt, hash, suspended FROM members WHERE cid = @cid", new { cid });
        if (row == null || row.Suspended != 0 || !PasswordHasher.Verify(password, row.Salt, row.Hash)) return null;
        // Members created with skynet-admin have no profile row yet.
        c.Execute("""
            INSERT INTO member_profiles (cid, registered_at, last_login_at) VALUES (@cid, @now, @now)
            ON CONFLICT(cid) DO UPDATE SET last_login_at = @now
            """, new { cid, now = Database.Now() });
        return Find(cid);
    }

    private sealed class Credentials
    {
        public byte[] Salt { get; set; } = [];
        public byte[] Hash { get; set; } = [];
        public long Suspended { get; set; }
    }

    /// <summary>Password check only (ignores suspension), to tell a suspended member why they cannot log in.</summary>
    public bool PasswordMatches(long cid, string password)
    {
        using var c = db.Open();
        var row = c.QuerySingleOrDefault<Credentials>("SELECT salt, hash, suspended FROM members WHERE cid = @cid", new { cid });
        return row != null && PasswordHasher.Verify(password, row.Salt, row.Hash);
    }

    public bool IsSuspended(long cid)
    {
        using var c = db.Open();
        return c.ExecuteScalar<long>("SELECT suspended FROM members WHERE cid = @cid", new { cid }) != 0;
    }

    public void ChangePassword(long cid, string password)
    {
        var (salt, hash) = PasswordHasher.Hash(password);
        using var c = db.Open();
        c.Execute("UPDATE members SET salt = @salt, hash = @hash WHERE cid = @cid", new { cid, salt, hash });
    }

    public void UpdateProfile(long cid, string email, string country)
    {
        using var c = db.Open();
        c.Execute("""
            INSERT INTO member_profiles (cid, email, country, registered_at) VALUES (@cid, @email, @country, @now)
            ON CONFLICT(cid) DO UPDATE SET email = @email, country = @country
            """, new { cid, email, country, now = Database.Now() });
    }

    public IReadOnlyList<string> RolesOf(long cid)
    {
        using var c = db.Open();
        return c.Query<string>("SELECT role FROM staff_roles WHERE cid = @cid ORDER BY role", new { cid }).ToList();
    }

    public IReadOnlyList<Member> Search(string? query, bool suspendedOnly = false, int limit = 100)
    {
        using var c = db.Open();
        query = (query ?? "").Trim();
        string filter = suspendedOnly ? " AND m.suspended = 1" : "";
        if (query.Length == 0)
            return c.Query<Member>(Select + " WHERE 1 = 1" + filter + " ORDER BY m.cid DESC LIMIT @limit", new { limit }).ToList();
        return c.Query<Member>(Select + """
             WHERE (CAST(m.cid AS TEXT) = @query OR m.name LIKE @like OR p.email LIKE @like)
            """ + filter + " ORDER BY m.cid DESC LIMIT @limit", new { query, like = "%" + query + "%", limit }).ToList();
    }

    public IReadOnlyList<Member> Staff()
    {
        using var c = db.Open();
        return c.Query<Member>(Select + """
             WHERE m.rating >= 8 OR m.cid IN (SELECT cid FROM staff_roles) ORDER BY m.rating DESC, m.cid
            """).ToList();
    }

    public long Count()
    {
        using var c = db.Open();
        return c.ExecuteScalar<long>("SELECT COUNT(*) FROM members");
    }

    // ---- staff actions (always audited) ----------------------------------------------------------

    public void SetRating(long actor, long cid, int rating)
    {
        using var c = db.Open();
        int old = c.ExecuteScalar<int>("SELECT rating FROM members WHERE cid = @cid", new { cid });
        c.Execute("UPDATE members SET rating = @rating WHERE cid = @cid", new { cid, rating });
        audit.Log(actor, "rating", cid.ToString(), $"{Ratings.Short(old)} → {Ratings.Short(rating)}");
    }

    /// <summary>
    /// Suspends (for <paramref name="days"/>, or for good when null) or lifts a suspension. The FSD
    /// server reads the same flag: it refuses the login and disconnects a member already online.
    /// </summary>
    public void SetSuspended(long actor, long cid, bool suspended, string reason, int? days = null)
    {
        long now = Database.Now();
        long? until = suspended && days is > 0 ? now + days.Value * 86400L : null;
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        c.Execute("UPDATE members SET suspended = @s WHERE cid = @cid", new { cid, s = suspended ? 1 : 0 }, tx);
        c.Execute("""
            INSERT INTO member_profiles (cid, registered_at, suspension_reason, suspended_until) VALUES (@cid, @now, @reason, @until)
            ON CONFLICT(cid) DO UPDATE SET suspension_reason = @reason, suspended_until = @until
            """, new { cid, now, reason = suspended ? reason : "", until }, tx);
        tx.Commit();
        string details = suspended ? (days is > 0 ? $"{days} d: {reason}" : $"permanent: {reason}") : reason;
        audit.Log(actor, suspended ? "suspend" : "unsuspend", cid.ToString(), details);
    }

    /// <summary>Lifts temporary suspensions whose time is up; returns the CIDs released.</summary>
    public IReadOnlyList<long> LiftExpiredSuspensions()
    {
        using var c = db.Open();
        var due = c.Query<long>("""
            SELECT m.cid FROM members m JOIN member_profiles p ON p.cid = m.cid
            WHERE m.suspended = 1 AND p.suspended_until IS NOT NULL AND p.suspended_until <= @now
            """, new { now = Database.Now() }).ToList();
        foreach (var cid in due) SetSuspended(0, cid, false, "suspension expired");
        return due;
    }

    public void SetPilotRatings(long actor, long cid, int pilot, int military)
    {
        var old = Find(cid);
        if (old == null) return;
        using var c = db.Open();
        c.Execute("""
            INSERT INTO member_profiles (cid, registered_at, pilot_rating, military_rating) VALUES (@cid, @now, @pilot, @military)
            ON CONFLICT(cid) DO UPDATE SET pilot_rating = @pilot, military_rating = @military
            """, new { cid, now = Database.Now(), pilot, military });
        if (old.PilotRating != pilot)
            audit.Log(actor, "pilot-rating", cid.ToString(), $"{PilotRatings.Pilot.Short(old.PilotRating)} → {PilotRatings.Pilot.Short(pilot)}");
        if (old.MilitaryRating != military)
            audit.Log(actor, "military-rating", cid.ToString(), $"{PilotRatings.Military.Short(old.MilitaryRating)} → {PilotRatings.Military.Short(military)}");
    }

    public void ResetPassword(long actor, long cid, string password)
    {
        ChangePassword(cid, password);
        audit.Log(actor, "password-reset", cid.ToString());
    }

    public void SetRoles(long actor, long cid, IEnumerable<string> roles)
    {
        var list = roles.Where(Permissions.Roles.ContainsKey).Distinct().ToList();
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        c.Execute("DELETE FROM staff_roles WHERE cid = @cid", new { cid }, tx);
        foreach (var role in list) c.Execute("INSERT INTO staff_roles (cid, role) VALUES (@cid, @role)", new { cid, role }, tx);
        tx.Commit();
        audit.Log(actor, "roles", cid.ToString(), list.Count == 0 ? "—" : string.Join(", ", list));
    }

    public IReadOnlyList<StaffNote> Notes(long cid)
    {
        using var c = db.Open();
        return c.Query<StaffNote>("""
            SELECT n.*, COALESCE(m.name, '') AS author_name FROM staff_notes n LEFT JOIN members m ON m.cid = n.author_cid
            WHERE n.cid = @cid ORDER BY n.id DESC
            """, new { cid }).ToList();
    }

    public void AddNote(long actor, long cid, string body)
    {
        using var c = db.Open();
        c.Execute("INSERT INTO staff_notes (cid, author_cid, body, created_at) VALUES (@cid, @actor, @body, @now)",
            new { cid, actor, body, now = Database.Now() });
        audit.Log(actor, "note", cid.ToString());
    }
}
