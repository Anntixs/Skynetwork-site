using Dapper;
using Microsoft.Extensions.Options;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Data;

public sealed class MemberService(Database db, IOptions<SiteOptions> options, AuditService audit)
{
    private const string Select = """
        SELECT m.cid, m.name, m.rating, m.suspended, p.email, COALESCE(p.country, '') AS country,
               p.registered_at, p.last_login_at, COALESCE(p.suspension_reason, '') AS suspension_reason
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

    public IReadOnlyList<Member> Search(string? query, int limit = 100)
    {
        using var c = db.Open();
        query = (query ?? "").Trim();
        if (query.Length == 0)
            return c.Query<Member>(Select + " ORDER BY m.cid DESC LIMIT @limit", new { limit }).ToList();
        return c.Query<Member>(Select + """
             WHERE CAST(m.cid AS TEXT) = @query OR m.name LIKE @like OR p.email LIKE @like
             ORDER BY m.cid DESC LIMIT @limit
            """, new { query, like = "%" + query + "%", limit }).ToList();
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

    public void SetSuspended(long actor, long cid, bool suspended, string reason)
    {
        using var c = db.Open();
        c.Execute("UPDATE members SET suspended = @s WHERE cid = @cid", new { cid, s = suspended ? 1 : 0 });
        c.Execute("""
            INSERT INTO member_profiles (cid, registered_at, suspension_reason) VALUES (@cid, @now, @reason)
            ON CONFLICT(cid) DO UPDATE SET suspension_reason = @reason
            """, new { cid, now = Database.Now(), reason = suspended ? reason : "" });
        audit.Log(actor, suspended ? "suspend" : "unsuspend", cid.ToString(), reason);
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
