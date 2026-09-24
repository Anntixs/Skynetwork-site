using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SkyNetwork.Site.Data;

namespace SkyNetwork.Site.Tests;

public class DivisionTests
{
    private static HttpClient Api(SiteFactory site, string key)
    {
        var c = site.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return c;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    private const string Probe = "/api/division/v1/rating-requests";

    /// <summary>A SKYRUS key, as an administrator would issue it.</summary>
    private static (Division Division, string Key) Skyrus(SiteFactory site)
    {
        var divisions = site.Get<DivisionService>();
        var (key, _) = divisions.IssueKey(0, "SKYRUS", "SkyRUS");
        return (divisions.FindByCode("SKYRUS")!, key!);
    }

    [Fact]
    public async Task Api_NeedsAValidKey()
    {
        using var site = new SiteFactory();
        var (d, key) = Skyrus(site);
        Assert.Equal(HttpStatusCode.Unauthorized, (await site.CreateClient().GetAsync(Probe)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api(site, "skd_wrong").GetAsync(Probe)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Api(site, key).GetAsync(Probe)).StatusCode);

        // X-Api-Key works too; a new key replaces the old one; a revoked key stops working.
        var x = site.CreateClient();
        x.DefaultRequestHeaders.Add("X-Api-Key", key);
        Assert.Equal(HttpStatusCode.OK, (await x.GetAsync(Probe)).StatusCode);
        var divisions = site.Get<DivisionService>();
        string newKey = divisions.IssueKey(0, "skyrus").Key!;
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api(site, key).GetAsync(Probe)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Api(site, newKey).GetAsync(Probe)).StatusCode);
        Assert.Single(divisions.All());
        divisions.RevokeKey(0, d.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api(site, newKey).GetAsync(Probe)).StatusCode);
        Assert.Equal("The code is 3–12 Latin letters or digits, e.g. SKYRUS", divisions.IssueKey(0, "a b").Error);
    }

    [Fact]
    public async Task ExamPassed_DivisionRequests_SupervisorApproves()
    {
        using var site = new SiteFactory();
        var (d, key) = Skyrus(site);
        long student = site.Member("Petr Student", Ratings.S1);
        long examiner = site.Member("Ivan Examiner", Ratings.I1);
        long sup = site.Member("Sergey Supervisor", Ratings.SUP);

        // The member applied for training on the site; the division trained and examined them.
        var s = site.Browser();
        await s.LoginAsync(student);
        await s.SubmitAsync("/training", new Dictionary<string, string> { ["Track"] = "atc", ["Target"] = Ratings.S2.ToString(), ["Text"] = "Evenings" });
        long appId = site.Get<SupportService>().Training(student).Single().Id;
        var api = Api(site, key);

        // Exam passed: the division asks for S2.
        var body = new { cid = student, track = "atc", rating = "S2", examinerCid = examiner, examDate = "2026-09-20", score = "92%", externalId = "exam-1" };
        var created = await api.PostAsJsonAsync("/api/division/v1/rating-requests", body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var request = await Json(created);
        long id = request.GetProperty("id").GetInt64();
        Assert.Equal(("pending", "S1", "S2"), (request.GetProperty("status").GetString(), request.GetProperty("current").GetString(), request.GetProperty("rating").GetString()));

        // A retry with the same externalId returns the same request; another one is refused while pending.
        var retry = await api.PostAsJsonAsync("/api/division/v1/rating-requests", body);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(id, (await Json(retry)).GetProperty("id").GetInt64());
        var dup = await api.PostAsJsonAsync("/api/division/v1/rating-requests", body with { externalId = "exam-2" });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        Assert.Equal("request_pending", (await Json(dup)).GetProperty("error").GetString());

        // Nothing changes before a supervisor approves.
        Assert.Equal(Ratings.S1, site.Get<MemberService>().Find(student)!.Rating);

        var v = site.Browser();
        await v.LoginAsync(sup);
        var queue = await v.HtmlAsync("/staff/ratings");
        Assert.Contains("Petr Student", queue);
        Assert.Contains("92%", queue);
        await v.SubmitPageFormAsync("/staff/ratings", "Approve", new Dictionary<string, string> { ["id"] = id.ToString(), ["comment"] = "Well done" });

        Assert.Equal(Ratings.S2, site.Get<MemberService>().Find(student)!.Rating);
        var done = await Json(await api.GetAsync($"/api/division/v1/rating-requests/{id}"));
        Assert.Equal(("approved", sup, "Well done"),
            (done.GetProperty("status").GetString(), done.GetProperty("reviewer").GetInt64(), done.GetProperty("reviewComment").GetString()));
        Assert.Equal("completed", site.Get<SupportService>().TrainingRequest(appId)!.Status);
        Assert.Contains(site.Get<AuditService>().Recent(), e => e.Action == "rating" && e.ActorCid == sup && e.Details == "S1 → S2");
        Assert.Contains(site.Get<AuditService>().Recent(), e => e.Action == "rating-request" && e.Details == "SKYRUS: S2 approved");
    }

    [Fact]
    public async Task Requests_AreChecked()
    {
        using var site = new SiteFactory();
        var (d, key) = Skyrus(site);
        var divisions = site.Get<DivisionService>();
        long member = site.Member("Anna Member", Ratings.S2);
        long suspended = site.Member("Olga Suspended");
        site.Get<MemberService>().SetSuspended(0, suspended, true, "test");
        var api = Api(site, key);

        async Task<(HttpStatusCode, string?)> Post(object body)
        {
            var r = await api.PostAsJsonAsync("/api/division/v1/rating-requests", body);
            return (r.StatusCode, r.IsSuccessStatusCode ? null : (await Json(r)).GetProperty("error").GetString());
        }

        Assert.Equal((HttpStatusCode.NotFound, "member_not_found"), await Post(new { cid = 9999, track = "atc", rating = "S3" }));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "member_suspended"), await Post(new { cid = suspended, track = "atc", rating = "S1" }));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "rating_not_higher"), await Post(new { cid = member, track = "atc", rating = "S1" }));
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_rating"), await Post(new { cid = member, track = "atc", rating = "XX" }));
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_track"), await Post(new { cid = member, track = "space", rating = "S3" }));
        Assert.Equal((HttpStatusCode.Created, null), await Post(new { cid = member, track = "pilot", rating = "PPL" }));

        // Withdrawn by the division: no longer pending, a new one can be sent.
        var list = await Json(await api.GetAsync("/api/division/v1/rating-requests?status=pending"));
        long id = list[0].GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.OK, (await api.DeleteAsync($"/api/division/v1/rating-requests/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await api.DeleteAsync($"/api/division/v1/rating-requests/{id}")).StatusCode);
        Assert.Equal((HttpStatusCode.Created, null), await Post(new { cid = member, track = "pilot", rating = "PPL" }));

        // Another division's requests are invisible.
        string eudKey = divisions.IssueKey(0, "SKYEUD").Key!;
        Assert.Equal(HttpStatusCode.NotFound, (await Api(site, eudKey).GetAsync($"/api/division/v1/rating-requests/{id}")).StatusCode);
        Assert.Equal(0, (await Json(await Api(site, eudKey).GetAsync("/api/division/v1/rating-requests"))).GetArrayLength());
    }

    [Fact]
    public async Task InstructorRatings_OnlyAdministratorsApprove_AndDeclineNeedsAReason()
    {
        using var site = new SiteFactory();
        var (d, key) = Skyrus(site);
        var divisions = site.Get<DivisionService>();
        long c3 = site.Member("Maria Controller", Ratings.C3);
        long sup = site.Member("Sergey Supervisor", Ratings.SUP);
        long admin = site.Member("Anna Admin", Ratings.ADM);
        var r = await Api(site, key).PostAsJsonAsync("/api/division/v1/rating-requests", new { cid = c3, track = "atc", rating = "I1" });
        long id = (await Json(r)).GetProperty("id").GetInt64();

        var v = site.Browser();
        await v.LoginAsync(sup);
        Assert.DoesNotContain("handler=Approve", await v.HtmlAsync("/staff/ratings"));
        var forced = await v.SubmitAsync("/staff/ratings", new Dictionary<string, string> { ["id"] = id.ToString() }, "/staff/ratings?handler=Approve");
        Assert.Contains("Only an administrator approves instructor ratings", await forced.Content.ReadAsStringAsync());
        Assert.Equal(Ratings.C3, site.Get<MemberService>().Find(c3)!.Rating);

        // Declining without a reason is refused; with one it goes back to the division.
        await v.SubmitPageFormAsync("/staff/ratings", "Decline", new Dictionary<string, string> { ["id"] = id.ToString(), ["comment"] = "" });
        Assert.Equal("pending", divisions.Request(id)!.Status);

        var a = site.Browser();
        await a.LoginAsync(admin);
        await a.SubmitPageFormAsync("/staff/ratings", "Approve", new Dictionary<string, string> { ["id"] = id.ToString() });
        Assert.Equal(Ratings.I1, site.Get<MemberService>().Find(c3)!.Rating);
    }

    [Fact]
    public async Task Administrator_IssuesKeys_InTheRequestsSection()
    {
        using var site = new SiteFactory();
        long admin = site.Member("Anna Admin", Ratings.ADM);
        long sup = site.Member("Sergey Supervisor", Ratings.SUP);
        var a = site.Browser();
        await a.LoginAsync(admin);

        var r = await a.SubmitPageFormAsync("/staff/ratings", "Key", new Dictionary<string, string> { ["divisionCode"] = "skyasia", ["name"] = "SkyASIA" });
        var page = await r.Content.ReadAsStringAsync();
        var key = System.Text.RegularExpressions.Regex.Match(page, "skd_[A-Za-z0-9_-]{40}").Value;
        Assert.NotEmpty(key);
        Assert.Equal("SkyASIA", site.Get<DivisionService>().FindByCode("SKYASIA")!.Name);
        Assert.Equal(HttpStatusCode.OK, (await Api(site, key).GetAsync(Probe)).StatusCode);
        // Shown once: the page afterwards only has the hint.
        Assert.DoesNotContain(key, await a.HtmlAsync("/staff/ratings"));

        // Supervisors see the requests but not the keys, and cannot issue them.
        long student = site.Member("Petr Student");
        var divisions = site.Get<DivisionService>();
        Assert.Null(divisions.Submit(divisions.FindByCode("SKYASIA")!, new RatingRequestInput(student, "atc", "S1")).Error);
        var s = site.Browser();
        await s.LoginAsync(sup);
        Assert.DoesNotContain("handler=Key", await s.HtmlAsync("/staff/ratings"));
        var forced = await s.SubmitAsync("/staff/ratings", new Dictionary<string, string> { ["divisionCode"] = "HACK" }, "/staff/ratings?handler=Key");
        Assert.Equal(HttpStatusCode.NotFound, forced.StatusCode);
        Assert.Null(site.Get<DivisionService>().FindByCode("HACK"));
        // No separate division pages.
        Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync("/staff/divisions")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync("/divisions")).StatusCode);
    }
}
