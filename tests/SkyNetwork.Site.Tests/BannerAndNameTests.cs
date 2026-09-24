using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Services;

namespace SkyNetwork.Site.Tests;

public class BannerTests
{
    // A real 1×1 PNG.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private static async Task<HttpResponseMessage> PostMultipart(HttpClient c, string page, IDictionary<string, string> fields,
        (string Name, byte[] Bytes)? file = null)
    {
        var html = await (await c.GetAsync(page)).Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        Assert.True(token.Success);
        var form = new MultipartFormDataContent { { new StringContent(WebUtility.HtmlDecode(token.Groups[1].Value)), "__RequestVerificationToken" } };
        foreach (var (k, v) in fields) form.Add(new StringContent(v), k);
        if (file is { } f)
        {
            var content = new ByteArrayContent(f.Bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(content, "banner", f.Name);
        }
        return await c.PostAsync(page, form);
    }

    private static Dictionary<string, string> Event(string title) => new()
    {
        ["title"] = title, ["summary"] = "", ["airports"] = "UUEE", ["body"] = "Come fly",
        ["startDate"] = DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd"), ["startTime"] = "16:00",
        ["endDate"] = DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd"), ["endTime"] = "20:00", ["published"] = "true",
    };

    [Fact]
    public async Task EventBanner_Upload_Show_Replace_Remove_Delete()
    {
        using var site = new SiteFactory();
        long sup = site.Member("Sergey Supervisor", Ratings.SUP);
        var s = site.Browser();
        await s.LoginAsync(sup);

        var r = await PostMultipart(s, "/staff/events/new", Event("Moscow Fly-in"), ("banner.png", Png));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var e = Assert.Single(site.Get<ContentService>().AllEvents());
        Assert.Matches("^[a-f0-9]{32}\\.png$", e.Banner);
        var uploads = site.Get<UploadStore>();
        Assert.True(File.Exists(Path.Combine(uploads.Directory, e.Banner)));

        // Shown on the event page and in the lists, served as an image.
        Assert.Contains($"/uploads/{e.Banner}", await site.Browser().HtmlAsync($"/events/{e.Id}"));
        Assert.Contains($"/uploads/{e.Banner}", await site.Browser().HtmlAsync("/events"));
        var img = await site.Browser().GetAsync($"/uploads/{e.Banner}");
        Assert.Equal("image/png", img.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Png, await img.Content.ReadAsByteArrayAsync());

        // Something that is not an image is refused, whatever it is called; the old banner stays.
        var bad = await PostMultipart(s, $"/staff/events/{e.Id}", Event("Moscow Fly-in"), ("evil.png", "<script>x</script>"u8.ToArray()));
        Assert.Contains("Only JPEG, PNG and WebP", await bad.Content.ReadAsStringAsync());
        Assert.Equal(e.Banner, site.Get<ContentService>().Event(e.Id)!.Banner);

        // A new one replaces it and the old file goes; then it is removed.
        await PostMultipart(s, $"/staff/events/{e.Id}", Event("Moscow Fly-in"), ("new.png", Png));
        string second = site.Get<ContentService>().Event(e.Id)!.Banner;
        Assert.NotEqual(e.Banner, second);
        Assert.False(File.Exists(Path.Combine(uploads.Directory, e.Banner)));
        await PostMultipart(s, $"/staff/events/{e.Id}", new Dictionary<string, string>(Event("Moscow Fly-in")) { ["removeBanner"] = "true" });
        Assert.Equal("", site.Get<ContentService>().Event(e.Id)!.Banner);
        Assert.False(File.Exists(Path.Combine(uploads.Directory, second)));

        Assert.Equal(HttpStatusCode.NotFound, (await site.Browser().GetAsync("/uploads/../skynet.db")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await site.Browser().GetAsync($"/uploads/{second}")).StatusCode);
    }

    [Fact]
    public async Task NewsBanner()
    {
        using var site = new SiteFactory();
        long editor = site.Member("News Editor");
        site.Get<MemberService>().SetRoles(0, editor, ["news"]);
        var c = site.Browser();
        await c.LoginAsync(editor);
        var r = await PostMultipart(c, "/staff/news/new",
            new Dictionary<string, string> { ["title"] = "New radar", ["body"] = "Network-ATC 2.0", ["published"] = "true" }, ("radar.png", Png));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var post = Assert.Single(site.Get<ContentService>().News());
        Assert.NotEqual("", post.Banner);
        Assert.Contains($"/uploads/{post.Banner}", await site.Browser().HtmlAsync("/news"));
        Assert.Contains($"/uploads/{post.Banner}", await site.Browser().HtmlAsync($"/news/{post.Id}"));
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "jpg")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, "webp")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38 }, null)] // GIF
    public void RecognisesImagesByContent(byte[] head, string? ext) => Assert.Equal(ext, UploadStore.Sniff(head));
}

public class NameTests
{
    [Fact]
    public async Task SupervisorRenamesMembers_ButNotAdministrators()
    {
        using var site = new SiteFactory();
        long sup = site.Member("Sergey Supervisor", Ratings.SUP);
        long admin = site.Member("Anna Admin", Ratings.ADM);
        long member = site.Member("Ivan Petrof");
        var s = site.Browser();
        await s.LoginAsync(sup);

        var r = await s.SubmitPageFormAsync($"/staff/members/{member}", "Name", new Dictionary<string, string> { ["name"] = "  Ivan   Petrov " });
        Assert.Contains("Name changed", await r.Content.ReadAsStringAsync());
        Assert.Equal("Ivan Petrov", site.Get<MemberService>().Find(member)!.Name);
        Assert.Contains(site.Get<AuditService>().Recent(), a => a.Action == "name" && a.Details == "Ivan Petrof → Ivan Petrov");

        var bad = await s.SubmitPageFormAsync($"/staff/members/{member}", "Name", new Dictionary<string, string> { ["name"] = "Ivan" });
        Assert.Contains("Enter your first and last name", await bad.Content.ReadAsStringAsync());

        Assert.DoesNotContain("handler=Name", await s.HtmlAsync($"/staff/members/{admin}"));
        var denied = await s.SubmitAsync($"/staff/members/{admin}", new Dictionary<string, string> { ["name"] = "Someone Else" },
            $"/staff/members/{admin}?handler=Name");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Equal("Anna Admin", site.Get<MemberService>().Find(admin)!.Name);

        // Administrators rename anyone.
        var a = site.Browser();
        await a.LoginAsync(admin);
        await a.SubmitPageFormAsync($"/staff/members/{sup}", "Name", new Dictionary<string, string> { ["name"] = "Sergey Ivanov" });
        Assert.Equal("Sergey Ivanov", site.Get<MemberService>().Find(sup)!.Name);
    }

    [Fact]
    public async Task InstructorsCannotRename()
    {
        using var site = new SiteFactory();
        long instructor = site.Member("Ilya Instructor", Ratings.I1);
        long member = site.Member("Ivan Petrov");
        var c = site.Browser();
        await c.LoginAsync(instructor);
        Assert.DoesNotContain("handler=Name", await c.HtmlAsync($"/staff/members/{member}"));
    }
}
