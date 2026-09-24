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

    /// <summary>The whole &lt;form&gt; element that contains <paramref name="marker"/>.</summary>
    private static string FormWith(string html, string marker)
    {
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(html, "<form[^>]*>.*?</form>", System.Text.RegularExpressions.RegexOptions.Singleline))
            if (m.Value.Contains(marker)) return m.Value;
        Assert.Fail($"no form with {marker}");
        return "";
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    /// <summary>The seeded SKYRUS division with a fresh key, as an administrator would set it up.</summary>
    private static (Division Division, string Key) Skyrus(SiteFactory site)
    {
        var divisions = site.Get<DivisionService>();
        var d = divisions.FindByCode("SKYRUS")!;
        return (d, divisions.IssueKey(0, d.Id));
    }

    [Fact]
    public async Task FirstDivisionsExist_AndArePublic()
    {
        using var site = new SiteFactory();
        var list = await site.Browser().HtmlAsync("/divisions");
        Assert.Contains("SKYRUS", list);
        Assert.Contains("SKYEUD", list);
        await site.Browser().HtmlAsync("/divisions/SKYRUS");
        Assert.Equal(HttpStatusCode.NotFound, (await site.Browser().GetAsync("/divisions/NOPE")).StatusCode);
        var json = await Json(await site.Browser().GetAsync("/api/v1/divisions"));
        Assert.Equal(2, json.GetArrayLength());
    }

    [Fact]
    public async Task Api_NeedsAValidKey()
    {
        using var site = new SiteFactory();
        var (d, key) = Skyrus(site);
        Assert.Equal(HttpStatusCode.Unauthorized, (await site.CreateClient().GetAsync("/api/division/v1/division")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api(site, "skd_wrong").GetAsync("/api/division/v1/division")).StatusCode);

        var ok = await Api(site, key).GetAsync("/api/division/v1/division");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("SKYRUS", (await Json(ok)).GetProperty("code").GetString());

        // X-Api-Key works too; a new key replaces the old one; an inactive division is locked out.
        var x = site.CreateClient();
        x.DefaultRequestHeaders.Add("X-Api-Key", key);
        Assert.Equal(HttpStatusCode.OK, (await x.GetAsync("/api/division/v1/division")).StatusCode);
        var divisions = site.Get<DivisionService>();
        string newKey = divisions.IssueKey(0, d.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api(site, key).GetAsync("/api/division/v1/division")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Api(site, newKey).GetAsync("/api/division/v1/division")).StatusCode);
        divisions.Update(0, d.Id, d.Name, d.Region, d.Website, d.Description, null, active: false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api(site, newKey).GetAsync("/api/division/v1/division")).StatusCode);
    }

    [Fact]
    public async Task ExamPassed_DivisionRequests_SupervisorApproves()
    {
        using var site = new SiteFactory();
        var (d, key) = Skyrus(site);
        long student = site.Member("Petr Student", Ratings.S1);
        long examiner = site.Member("Ivan Examiner", Ratings.I1);
        long sup = site.Member("Sergey Supervisor", Ratings.SUP);

        // The member joins the division and applies to its academy on the network site.
        var s = site.Browser();
        await s.LoginAsync(student);
        var joined = await s.SubmitPageFormAsync("/training", "Division", new Dictionary<string, string> { ["divisionId"] = d.Id.ToString() });
        Assert.Equal("SKYRUS", site.Get<DivisionService>().Of(student)?.Code);
        // The page is now at ?handler=Division: the application form must not post there (that would leave the division).
        var apply = FormWith(await joined.Content.ReadAsStringAsync(), "name=\"Track\"");
        Assert.Contains("action=\"/training\"", apply);
        Assert.Contains("__RequestVerificationToken", apply);
        await s.SubmitAsync("/training", new Dictionary<string, string> { ["Track"] = "atc", ["Target"] = Ratings.S2.ToString(), ["Text"] = "Evenings" });

        // The academy sees the application through the API and takes it.
        var api = Api(site, key);
        var apps = await Json(await api.GetAsync("/api/division/v1/training-requests"));
        Assert.Equal(1, apps.GetArrayLength());
        long appId = apps[0].GetProperty("id").GetInt64();
        Assert.Equal("S2", apps[0].GetProperty("target").GetString());
        var taken = await api.PostAsJsonAsync($"/api/division/v1/training-requests/{appId}", new { status = "accepted", instructorCid = examiner });
        Assert.Equal(HttpStatusCode.OK, taken.StatusCode);

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

        // Nothing changes before a supervisor approves; the member sees the request.
        Assert.Equal(Ratings.S1, site.Get<MemberService>().Find(student)!.Rating);
        Assert.Contains("SKYRUS", await s.HtmlAsync("/training"));

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
        long outsider = site.Member("Olga Outsider");
        divisions.SetMemberDivision(member, member, d.Id);
        divisions.SetMemberDivision(outsider, outsider, divisions.FindByCode("SKYEUD")!.Id);
        var api = Api(site, key);

        async Task<(HttpStatusCode, string?)> Post(object body)
        {
            var r = await api.PostAsJsonAsync("/api/division/v1/rating-requests", body);
            return (r.StatusCode, r.IsSuccessStatusCode ? null : (await Json(r)).GetProperty("error").GetString());
        }

        Assert.Equal((HttpStatusCode.NotFound, "member_not_found"), await Post(new { cid = 9999, track = "atc", rating = "S3" }));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "not_division_member"), await Post(new { cid = outsider, track = "atc", rating = "S1" }));
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
        string eudKey = divisions.IssueKey(0, divisions.FindByCode("SKYEUD")!.Id);
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
        divisions.SetMemberDivision(c3, c3, d.Id);
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
    public async Task Administrator_CreatesDivision_AndIssuesKey()
    {
        using var site = new SiteFactory();
        long admin = site.Member("Anna Admin", Ratings.ADM);
        long sup = site.Member("Sergey Supervisor", Ratings.SUP);
        var a = site.Browser();
        await a.LoginAsync(admin);

        var created = await a.SubmitAsync("/staff/divisions", new Dictionary<string, string>
        {
            ["code"] = "skyasia", ["name"] = "SkyASIA", ["region"] = "Asia", ["website"] = "https://asia.example", ["description"] = "",
        });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var d = site.Get<DivisionService>().FindByCode("SKYASIA")!;
        Assert.Equal("Asia", d.Region);

        var page = await (await a.SubmitPageFormAsync($"/staff/divisions/{d.Id}", "Key", new Dictionary<string, string>())).Content.ReadAsStringAsync();
        var key = System.Text.RegularExpressions.Regex.Match(page, "skd_[A-Za-z0-9_-]{40}").Value;
        Assert.NotEmpty(key);
        // Saving the division from this page must not issue yet another key.
        var save = FormWith(page, "name=\"director\"");
        Assert.Contains($"action=\"/staff/divisions/{d.Id}\"", save);
        Assert.Contains("__RequestVerificationToken", save);
        Assert.Equal(HttpStatusCode.OK, (await Api(site, key).GetAsync("/api/division/v1/division")).StatusCode);
        // Shown once: the page afterwards only has the hint.
        Assert.DoesNotContain(key, await a.HtmlAsync($"/staff/divisions/{d.Id}"));

        // Supervisors approve ratings but do not manage divisions.
        var s = site.Browser();
        await s.LoginAsync(sup);
        Assert.Equal(HttpStatusCode.NotFound, (await s.GetAsync("/staff/divisions")).StatusCode);
        await s.HtmlAsync("/staff/ratings");
    }

    [Fact]
    public async Task TrainingNeedsADivision()
    {
        using var site = new SiteFactory();
        long m = site.Member("New Member");
        var c = site.Browser();
        await c.LoginAsync(m);
        var r = await c.SubmitAsync("/training", new Dictionary<string, string> { ["Track"] = "atc", ["Target"] = Ratings.S1.ToString() });
        Assert.Contains("Choose your division first", await r.Content.ReadAsStringAsync());
        Assert.Empty(site.Get<SupportService>().Training(m));
    }
}
