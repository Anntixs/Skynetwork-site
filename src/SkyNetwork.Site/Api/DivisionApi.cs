using SkyNetwork.Site.Data;

namespace SkyNetwork.Site.Api;

/// <summary>
/// API for division websites: after a student passes an exam in the division's academy, the
/// division sends a rating request; a supervisor approves it in the staff area.
/// Authenticated with the division's key: <c>Authorization: Bearer skd_…</c> (or <c>X-Api-Key</c>).
/// Documented on /developers.
/// </summary>
public static class DivisionApi
{
    private const string DivisionItem = "division";

    public static string? KeyOf(HttpContext ctx)
    {
        string? auth = ctx.Request.Headers.Authorization;
        if (auth != null && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return auth[7..].Trim();
        string? header = ctx.Request.Headers["X-Api-Key"];
        return string.IsNullOrWhiteSpace(header) ? null : header.Trim();
    }

    private static Division Current(HttpContext ctx) => (Division)ctx.Items[DivisionItem]!;

    public static void MapDivisionApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/division/v1").RequireRateLimiting("division-api");
        api.AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            http.Response.Headers.CacheControl = "no-store";
            var division = http.RequestServices.GetRequiredService<DivisionService>().Authenticate(KeyOf(http));
            if (division == null)
            {
                http.Response.Headers.WWWAuthenticate = "Bearer";
                return Problem(401, "invalid_key", "A valid division API key is required");
            }
            http.Items[DivisionItem] = division;
            return await next(ctx);
        });

        // Sign-in on a division website with the network CID and password (checked here, never stored
        // there). Failed attempts per CID are limited to slow down password guessing.
        api.MapPost("/auth", (AuthInput body, MemberService members) =>
        {
            if (body.Cid <= 0 || string.IsNullOrEmpty(body.Password)) return Problem(400, "invalid_input", "cid and password are required");
            if (!Failures.Allowed(body.Cid)) return Problem(429, "too_many_attempts", "Too many failed attempts, try again in a few minutes");
            if (members.Authenticate(body.Cid, body.Password) is { } m)
            {
                Failures.Reset(body.Cid);
                return Results.Ok(new
                {
                    m.Cid, m.Name, m.Email, rating = m.RatingShort, staffRank = m.IsStaff ? Ratings.Short(m.StaffRank) : null,
                    pilotRating = PilotRatings.Pilot.Short(m.PilotRating), militaryRating = PilotRatings.Military.Short(m.MilitaryRating),
                });
            }
            Failures.Add(body.Cid);
            return members.PasswordMatches(body.Cid, body.Password)
                ? Problem(403, "member_suspended", "The member is suspended")
                : Problem(401, "invalid_credentials", "Wrong CID or password");
        });

        // Any member: an academy checks a CID before enrolling someone.
        api.MapGet("/members/{cid:long}", (long cid, MemberService members) =>
            members.Find(cid) is { } m ? Results.Ok(MemberDto(m)) : Problem(404, "member_not_found", "No member with this CID"));

        api.MapGet("/rating-requests", (HttpContext ctx, DivisionService divisions, string? status, long? cid) =>
            divisions.Requests(status is { Length: > 0 } && status != "all" ? status : null, Current(ctx).Id, cid).Select(RequestDto));

        api.MapGet("/rating-requests/{id:long}", (long id, HttpContext ctx, DivisionService divisions) =>
            divisions.Request(id) is { } r && r.DivisionId == Current(ctx).Id
                ? Results.Ok(RequestDto(r))
                : Problem(404, "not_found", "No such rating request"));

        // The exam is passed: ask for the rating. 201 for a new request, 200 when externalId was sent before.
        api.MapPost("/rating-requests", (RatingRequestInput body, HttpContext ctx, DivisionService divisions) =>
        {
            var (request, created, error) = divisions.Submit(Current(ctx), body);
            if (error != null) return Problem(error.Status, error.Code, error.Message);
            return created ? Results.Created($"/api/division/v1/rating-requests/{request!.Id}", RequestDto(request)) : Results.Ok(RequestDto(request!));
        });

        api.MapDelete("/rating-requests/{id:long}", (long id, HttpContext ctx, DivisionService divisions) =>
        {
            var r = divisions.Request(id);
            if (r == null || r.DivisionId != Current(ctx).Id) return Problem(404, "not_found", "No such rating request");
            return divisions.Withdraw(Current(ctx), id)
                ? Results.Ok(RequestDto(divisions.Request(id)!))
                : Problem(409, "not_pending", "Only a pending request can be withdrawn");
        });
    }

    public sealed record AuthInput(long Cid, string? Password);

    /// <summary>Failed sign-ins per CID: at most 10 in 10 minutes.</summary>
    private static class Failures
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, (int Count, DateTime Since)> Map = new();
        private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

        public static bool Allowed(long cid) =>
            !Map.TryGetValue(cid, out var f) || f.Count < 10 || DateTime.UtcNow - f.Since > Window;

        public static void Add(long cid) => Map.AddOrUpdate(cid, _ => (1, DateTime.UtcNow),
            (_, f) => DateTime.UtcNow - f.Since > Window ? (1, DateTime.UtcNow) : (f.Count + 1, f.Since));

        public static void Reset(long cid) => Map.TryRemove(cid, out _);
    }

    private static IResult Problem(int status, string code, string message) =>
        Results.Json(new { error = code, message }, statusCode: status);

    private static object MemberDto(Member m) => new
    {
        m.Cid, m.Name, rating = m.RatingShort,
        pilotRating = PilotRatings.Pilot.Short(m.PilotRating), militaryRating = PilotRatings.Military.Short(m.MilitaryRating),
        suspended = m.Suspended, registered = m.Registered,
    };

    public static object RequestDto(RatingRequest r) => new
    {
        r.Id, r.Cid, r.Name, division = r.DivisionCode, r.Track, current = r.CurrentShort, rating = r.TargetShort,
        examiner = r.ExaminerCid, r.ExaminerName, r.ExamDate, r.Score, r.ReportUrl, r.Comment, r.ExternalId, r.Status,
        reviewer = r.ReviewerCid, reviewComment = r.ReviewComment, created = r.Created, updated = r.Updated,
    };
}
