using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Data;

public sealed class Division
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Region { get; set; } = "";
    public string Website { get; set; } = "";
    public string Description { get; set; } = "";
    public long? DirectorCid { get; set; }
    public string DirectorName { get; set; } = "";
    public bool Active { get; set; }
    public string ApiKeyHint { get; set; } = "";
    public long? ApiKeyCreatedAt { get; set; }
    public long CreatedAt { get; set; }

    public bool HasApiKey => ApiKeyCreatedAt != null;
    public DateTime? ApiKeyCreated => ApiKeyCreatedAt is { } t ? Time.Utc(t) : null;
}

/// <summary>A rating a division asks for after its student passed the exam; a supervisor approves it.</summary>
public sealed class RatingRequest
{
    public long Id { get; set; }
    public long DivisionId { get; set; }
    public string DivisionCode { get; set; } = "";
    public string DivisionName { get; set; } = "";
    public long Cid { get; set; }
    public string Name { get; set; } = "";
    public string Track { get; set; } = "atc";
    /// <summary>The member's level on the track when the request came in.</summary>
    public int CurrentRating { get; set; }
    /// <summary>The member's level now (it may have changed since).</summary>
    public int MemberRating { get; set; }
    public int TargetRating { get; set; }
    public long? ExaminerCid { get; set; }
    public string ExaminerName { get; set; } = "";
    public string ExamDate { get; set; } = "";
    public string Score { get; set; } = "";
    public string ReportUrl { get; set; } = "";
    public string Comment { get; set; } = "";
    public string? ExternalId { get; set; }
    public string Status { get; set; } = "pending";
    public long? ReviewerCid { get; set; }
    public string ReviewerName { get; set; } = "";
    public string ReviewComment { get; set; } = "";
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }

    public string CurrentShort => TrainingTracks.Short(Track, CurrentRating);
    public string TargetShort => TrainingTracks.Short(Track, TargetRating);
    public DateTime Created => Time.Utc(CreatedAt);
    public DateTime Updated => Time.Utc(UpdatedAt);
}

/// <summary>What a division sends to ask for a rating (the JSON body of POST /api/division/v1/rating-requests).</summary>
public sealed record RatingRequestInput(
    long Cid, string? Track, string? Rating, long? ExaminerCid = null, string? ExaminerName = null, string? ExamDate = null,
    string? Score = null, string? ReportUrl = null, string? Comment = null, string? ExternalId = null);

/// <summary>Why a request was refused: an HTTP status, a machine-readable code and an English message.</summary>
public sealed record RequestError(int Status, string Code, string Message);

/// <summary>Divisions, their API keys and the rating requests they send after exams.</summary>
public sealed partial class DivisionService(Database db, MemberService members, AuditService audit)
{
    public static readonly IReadOnlyDictionary<string, string> RequestStatuses = new Dictionary<string, string>
    {
        ["pending"] = "awaiting approval", ["approved"] = "approved", ["declined"] = "declined", ["withdrawn"] = "withdrawn",
    };

    private const string DivisionSelect = """
        SELECT d.*, COALESCE(m.name, '') AS director_name
        FROM divisions d LEFT JOIN members m ON m.cid = d.director_cid
        """;

    public IReadOnlyList<Division> All(bool activeOnly = false)
    {
        using var c = db.Open();
        return c.Query<Division>(DivisionSelect + " WHERE (NOT @activeOnly OR d.active = 1) ORDER BY d.code", new { activeOnly }).ToList();
    }

    public Division? Find(long id)
    {
        using var c = db.Open();
        return c.QuerySingleOrDefault<Division>(DivisionSelect + " WHERE d.id = @id", new { id });
    }

    public Division? FindByCode(string code)
    {
        using var c = db.Open();
        return c.QuerySingleOrDefault<Division>(DivisionSelect + " WHERE d.code = @code", new { code });
    }

    // ---- management (administrators) ------------------------------------------------------------

    [GeneratedRegex("^[A-Z0-9]{3,12}$")]
    private static partial Regex CodePattern();

    /// <summary>Checks the fields; returns an English error or null.</summary>
    public static string? Validate(string code, string name, string website)
    {
        if (!CodePattern().IsMatch(code)) return "The code is 3–12 Latin letters or digits, e.g. SKYRUS";
        if (name.Length is < 2 or > 60) return "The name is 2 to 60 characters";
        if (website.Length > 0 && !(Uri.TryCreate(website, UriKind.Absolute, out var u) && u.Scheme is "http" or "https"))
            return "The website must be an http:// or https:// address";
        return null;
    }

    public (long Id, string? Error) Create(long actor, string code, string name, string region, string website, string description)
    {
        code = code.Trim().ToUpperInvariant();
        name = name.Trim();
        website = website.Trim();
        if (Validate(code, name, website) is { } error) return (0, error);
        if (FindByCode(code) != null) return (0, "A division with this code already exists");
        using var c = db.Open();
        long id = c.ExecuteScalar<long>("""
            INSERT INTO divisions (code, name, region, website, description, created_at)
            VALUES (@code, @name, @region, @website, @description, @now) RETURNING id
            """, new { code, name, region = region.Trim(), website, description = description.Trim(), now = Database.Now() });
        audit.Log(actor, "division", code, "created");
        return (id, null);
    }

    public string? Update(long actor, long id, string name, string region, string website, string description, long? directorCid, bool active)
    {
        var d = Find(id);
        if (d == null) return "Division not found";
        name = name.Trim();
        website = website.Trim();
        if (Validate(d.Code, name, website) is { } error) return error;
        if (directorCid is { } dc && members.Find(dc) == null) return "No member with this CID";
        using var c = db.Open();
        c.Execute("""
            UPDATE divisions SET name = @name, region = @region, website = @website, description = @description,
                   director_cid = @directorCid, active = @active WHERE id = @id
            """, new { id, name, region = region.Trim(), website, description = description.Trim(), directorCid, active = active ? 1 : 0 });
        audit.Log(actor, "division", d.Code, active ? "updated" : "updated, inactive");
        return null;
    }

    /// <summary>
    /// A new API key for the division, replacing the old one. Only its SHA-256 is stored: the key
    /// itself is returned once and shown to the administrator to pass on to the division.
    /// </summary>
    public string IssueKey(long actor, long id)
    {
        var d = Find(id) ?? throw new InvalidOperationException("no division");
        string key = "skd_" + Base64Url(RandomNumberGenerator.GetBytes(30));
        using var c = db.Open();
        c.Execute("UPDATE divisions SET api_key_hash = @hash, api_key_hint = @hint, api_key_created_at = @now WHERE id = @id",
            new { id, hash = Hash(key), hint = key[..8] + "…", now = Database.Now() });
        audit.Log(actor, "division-key", d.Code, "issued");
        return key;
    }

    public void RevokeKey(long actor, long id)
    {
        var d = Find(id);
        if (d == null) return;
        using var c = db.Open();
        c.Execute("UPDATE divisions SET api_key_hash = NULL, api_key_hint = '', api_key_created_at = NULL WHERE id = @id", new { id });
        audit.Log(actor, "division-key", d.Code, "revoked");
    }

    /// <summary>The active division the key belongs to, or null.</summary>
    public Division? Authenticate(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || !key.StartsWith("skd_", StringComparison.Ordinal) || key.Length > 100) return null;
        using var c = db.Open();
        // Keys are long random strings, so a plain hash lookup is safe (nothing to brute-force).
        return c.QuerySingleOrDefault<Division>(DivisionSelect + " WHERE d.api_key_hash = @hash AND d.active = 1", new { hash = Hash(key.Trim()) });
    }

    private static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // ---- rating requests ---------------------------------------------------------------------

    private const string RequestSelect = """
        SELECT r.*, d.code AS division_code, d.name AS division_name, COALESCE(m.name, '') AS name,
               COALESCE(v.name, '') AS reviewer_name,
               CASE r.track WHEN 'pilot' THEN COALESCE(p.pilot_rating, 0)
                            WHEN 'military' THEN COALESCE(p.military_rating, 0)
                            ELSE COALESCE(m.rating, 1) END AS member_rating
        FROM rating_requests r JOIN divisions d ON d.id = r.division_id
        LEFT JOIN members m ON m.cid = r.cid LEFT JOIN member_profiles p ON p.cid = r.cid
        LEFT JOIN members v ON v.cid = r.reviewer_cid
        """;

    public IReadOnlyList<RatingRequest> Requests(string? status = null, long? divisionId = null, long? cid = null, int limit = 300)
    {
        using var c = db.Open();
        return c.Query<RatingRequest>(RequestSelect + """
             WHERE (@status IS NULL OR r.status = @status) AND (@divisionId IS NULL OR r.division_id = @divisionId)
               AND (@cid IS NULL OR r.cid = @cid)
            ORDER BY r.status <> 'pending', r.created_at DESC LIMIT @limit
            """, new { status, divisionId, cid, limit }).ToList();
    }

    public RatingRequest? Request(long id)
    {
        using var c = db.Open();
        return c.QuerySingleOrDefault<RatingRequest>(RequestSelect + " WHERE r.id = @id", new { id });
    }

    public int PendingCount()
    {
        using var c = db.Open();
        return c.ExecuteScalar<int>("SELECT COUNT(*) FROM rating_requests WHERE status = 'pending'");
    }

    /// <summary>Parses "S2", "PPL", "M1" or a number for a track; null when it is not a level of that track.</summary>
    public static int? ParseLevel(string track, string? value)
    {
        value = (value ?? "").Trim().ToUpperInvariant();
        if (value.Length == 0) return null;
        if (track == "atc")
        {
            int r = int.TryParse(value, out var n) ? n : Ratings.FromShort(value);
            return Ratings.IsController(r) ? r : null;
        }
        var ladder = track == "pilot" ? PilotRatings.Pilot : PilotRatings.Military;
        if (int.TryParse(value, out var level)) return ladder.Valid(level) ? level : null;
        int i = ladder.Levels.ToList().FindIndex(l => l.Short.Equals(value, StringComparison.OrdinalIgnoreCase));
        return i >= 0 ? i : null;
    }

    /// <summary>
    /// A division asks for a rating for one of its members. Returns the request (an existing one
    /// when the same <c>externalId</c> was sent before) or why it was refused.
    /// </summary>
    public (RatingRequest? Request, bool Created, RequestError? Error) Submit(Division division, RatingRequestInput input)
    {
        string track = (input.Track ?? "atc").Trim().ToLowerInvariant();
        if (!TrainingTracks.Valid(track)) return Fail(400, "invalid_track", "track must be atc, pilot or military");
        string? externalId = string.IsNullOrWhiteSpace(input.ExternalId) ? null : input.ExternalId.Trim();
        if (externalId is { Length: > 100 }) return Fail(400, "invalid_external_id", "externalId is at most 100 characters");

        using var c = db.Open();
        if (externalId != null && c.ExecuteScalar<long?>("SELECT id FROM rating_requests WHERE division_id = @d AND external_id = @externalId",
                new { d = division.Id, externalId }) is { } existing)
            return (Request(existing), false, null); // a retry of the same request

        var member = members.Find(input.Cid);
        if (member == null) return Fail(404, "member_not_found", "No member with this CID");
        if (member.Suspended) return Fail(422, "member_suspended", "The member is suspended");
        if (ParseLevel(track, input.Rating) is not { } target) return Fail(400, "invalid_rating", "rating is not a rating of this track");
        int current = TrainingTracks.Current(track, member);
        if (target <= current) return Fail(422, "rating_not_higher", "The member already holds this rating or a higher one");
        if (input.ExaminerCid is { } ex && members.Find(ex) == null) return Fail(422, "examiner_not_found", "No member with the examiner's CID");
        string reportUrl = (input.ReportUrl ?? "").Trim();
        if (reportUrl.Length > 0 && !(Uri.TryCreate(reportUrl, UriKind.Absolute, out var u) && u.Scheme is "http" or "https"))
            return Fail(400, "invalid_report_url", "reportUrl must be an http(s) address");
        if (c.ExecuteScalar<long?>("SELECT id FROM rating_requests WHERE cid = @cid AND track = @track AND status = 'pending'",
                new { cid = member.Cid, track }) is { } pending)
            return Fail(409, "request_pending", $"Request {pending} for this member and track is still awaiting approval");

        long now = Database.Now();
        long id = c.ExecuteScalar<long>("""
            INSERT INTO rating_requests (division_id, cid, track, current_rating, target_rating, examiner_cid, examiner_name,
                exam_date, score, report_url, comment, external_id, status, created_at, updated_at)
            VALUES (@division, @cid, @track, @current, @target, @examinerCid, @examinerName, @examDate, @score, @reportUrl, @comment,
                @externalId, 'pending', @now, @now) RETURNING id
            """, new
        {
            division = division.Id, cid = member.Cid, track, current, target, examinerCid = input.ExaminerCid,
            examinerName = Clip(input.ExaminerName, 80), examDate = Clip(input.ExamDate, 40), score = Clip(input.Score, 40),
            reportUrl = Clip(reportUrl, 400), comment = Clip(input.Comment, 2000), externalId, now,
        });
        audit.Log(0, "rating-request", member.Cid.ToString(),
            $"{division.Code}: {TrainingTracks.Short(track, current)} → {TrainingTracks.Short(track, target)}");
        return (Request(id), true, null);

        static (RatingRequest?, bool, RequestError?) Fail(int status, string code, string message) => (null, false, new(status, code, message));
    }

    /// <summary>The division takes back a request that is still pending.</summary>
    public bool Withdraw(Division division, long id)
    {
        using var c = db.Open();
        bool done = c.Execute("""
            UPDATE rating_requests SET status = 'withdrawn', updated_at = @now WHERE id = @id AND division_id = @d AND status = 'pending'
            """, new { id, d = division.Id, now = Database.Now() }) > 0;
        if (done) audit.Log(0, "rating-request", id.ToString(), $"{division.Code}: withdrawn");
        return done;
    }

    /// <summary>
    /// A supervisor or administrator approves: the rating is given and the request closed. Returns an
    /// English error or null.
    /// </summary>
    public string? Approve(Member actor, Perm perms, long id, string comment)
    {
        var r = Request(id);
        if (r == null) return "Request not found";
        if (r.Status != "pending") return "This request has already been handled";
        if (r.Cid == actor.Cid) return "You cannot approve your own rating";
        if (!Permissions.CanApproveRating(actor.StaffRank, perms, r.Track, r.TargetRating)) return "You cannot approve this rating";
        var member = members.Find(r.Cid);
        if (member == null) return "Member not found";
        if (TrainingTracks.Current(r.Track, member) >= r.TargetRating) return "The member already holds this rating or a higher one";

        if (r.Track == "atc") members.SetRating(actor.Cid, r.Cid, r.TargetRating);
        else members.SetPilotRatings(actor.Cid, r.Cid,
            r.Track == "pilot" ? r.TargetRating : member.PilotRating,
            r.Track == "military" ? r.TargetRating : member.MilitaryRating);

        using var c = db.Open();
        long now = Database.Now();
        c.Execute("""
            UPDATE rating_requests SET status = 'approved', reviewer_cid = @actor, review_comment = @comment, updated_at = @now WHERE id = @id
            """, new { id, actor = actor.Cid, comment = Clip(comment, 1000), now });
        // The academy application this exam was for is finished too.
        c.Execute("""
            UPDATE training_requests SET status = 'completed', updated_at = @now
            WHERE cid = @cid AND track = @track AND target_rating <= @target AND status IN ('open', 'accepted')
            """, new { cid = r.Cid, track = r.Track, target = r.TargetRating, now });
        audit.Log(actor.Cid, "rating-request", r.Cid.ToString(), $"{r.DivisionCode}: {r.TargetShort} approved");
        return null;
    }

    public string? Decline(Member actor, Perm perms, long id, string comment)
    {
        var r = Request(id);
        if (r == null) return "Request not found";
        if (r.Status != "pending") return "This request has already been handled";
        if (!perms.HasFlag(Perm.ApproveRatings)) return "You cannot approve this rating";
        if (comment.Trim().Length == 0) return "Write the reason for the division";
        using var c = db.Open();
        c.Execute("""
            UPDATE rating_requests SET status = 'declined', reviewer_cid = @actor, review_comment = @comment, updated_at = @now WHERE id = @id
            """, new { id, actor = actor.Cid, comment = Clip(comment, 1000), now = Database.Now() });
        audit.Log(actor.Cid, "rating-request", r.Cid.ToString(), $"{r.DivisionCode}: {r.TargetShort} declined");
        return null;
    }

    private static string Clip(string? s, int max)
    {
        s = (s ?? "").Trim();
        return s.Length > max ? s[..max] : s;
    }
}
