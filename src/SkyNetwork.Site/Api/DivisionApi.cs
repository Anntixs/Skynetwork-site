using SkyNetwork.Site.Data;

namespace SkyNetwork.Site.Api;

/// <summary>
/// API for division websites: their academies read their members and applications and, after a
/// student passes an exam, send a rating request that a supervisor approves on the network site.
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

        api.MapGet("/division", (HttpContext ctx) => DivisionDto(Current(ctx)));

        // Members of the division (the academy's students).
        api.MapGet("/members", (HttpContext ctx, DivisionService divisions) =>
            divisions.Members(Current(ctx).Id).Select(m => MemberDto(m, Current(ctx))));

        // Any member: an academy checks a CID before enrolling someone.
        api.MapGet("/members/{cid:long}", (long cid, MemberService members, DivisionService divisions) =>
            members.Find(cid) is { } m ? Results.Ok(MemberDto(m, divisions.Of(cid))) : Problem(404, "member_not_found", "No member with this CID"));

        // Applications members filed for this division's academy on the network site.
        api.MapGet("/training-requests", (HttpContext ctx, SupportService support, string? status) =>
            support.Training(divisionId: Current(ctx).Id, activeOnly: status != "all").Select(TrainingDto));

        api.MapPost("/training-requests/{id:long}", (long id, TrainingUpdate body, HttpContext ctx, SupportService support) =>
        {
            var r = support.TrainingRequest(id);
            if (r == null || r.DivisionId != Current(ctx).Id) return Problem(404, "not_found", "No such training request");
            if (body.Status == null || !SupportService.TrainingStatuses.ContainsKey(body.Status))
                return Problem(400, "invalid_status", "status must be open, accepted, completed or declined");
            support.UpdateTraining(0, id, body.Status, (body.Comment ?? "").Trim(), body.InstructorCid);
            return Results.Ok(TrainingDto(support.TrainingRequest(id)!));
        });

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

    public sealed record TrainingUpdate(string? Status, string? Comment, long? InstructorCid);

    private static IResult Problem(int status, string code, string message) =>
        Results.Json(new { error = code, message }, statusCode: status);

    public static object DivisionDto(Division d) => new
    {
        d.Code, d.Name, d.Region, d.Website, d.Description, director = d.DirectorCid, members = d.MemberCount,
    };

    private static object MemberDto(Member m, Division? division) => new
    {
        m.Cid, m.Name, rating = m.RatingShort,
        pilotRating = PilotRatings.Pilot.Short(m.PilotRating), militaryRating = PilotRatings.Military.Short(m.MilitaryRating),
        division = division?.Code, suspended = m.Suspended, registered = m.Registered,
    };

    private static object TrainingDto(TrainingRequest r) => new
    {
        r.Id, r.Cid, r.Name, r.Track, current = r.CurrentShort, target = r.TargetShort, r.Message, r.Status,
        instructor = r.InstructorCid, comment = r.StaffComment, created = r.Created,
    };

    public static object RequestDto(RatingRequest r) => new
    {
        r.Id, r.Cid, r.Name, division = r.DivisionCode, r.Track, current = r.CurrentShort, rating = r.TargetShort,
        examiner = r.ExaminerCid, r.ExaminerName, r.ExamDate, r.Score, r.ReportUrl, r.Comment, r.ExternalId, r.Status,
        reviewer = r.ReviewerCid, reviewComment = r.ReviewComment, created = r.Created, updated = r.Updated,
    };
}
