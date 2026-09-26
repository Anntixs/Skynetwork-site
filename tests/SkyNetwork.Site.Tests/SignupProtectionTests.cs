using System.Net;
using System.Text.RegularExpressions;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Tests;

/// <summary>Junk registrations (numbers for names, swearing, made-up addresses, bots) are refused.</summary>
public class SignupProtectionTests
{
    private static Dictionary<string, string> Form(string name, string email) => new()
    {
        ["Name"] = name, ["Email"] = email, ["Country"] = "Russia", ["Password"] = "secret123", ["Confirm"] = "secret123",
    };

    // Like a browser: the form's own hidden "Started" field goes back with it, after the given pause.
    private static async Task<HttpResponseMessage> RegisterAsync(HttpClient c, Dictionary<string, string> fields, int pauseMs = 0)
    {
        string html = await c.GetStringAsync("/register");
        string token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value);
        string started = WebUtility.HtmlDecode(Regex.Match(html, "name=\"Started\" value=\"([^\"]*)\"").Groups[1].Value);
        Assert.NotEqual("", started);
        if (pauseMs > 0) await Task.Delay(pauseMs);
        return await c.PostAsync("/register", new FormUrlEncodedContent(
            new Dictionary<string, string>(fields) { ["__RequestVerificationToken"] = token, ["Started"] = started }));
    }

    [Theory]
    [InlineData("Ivan Petrov")]
    [InlineData("Иван Петров")]
    [InlineData("Мария Сукачёва")]
    [InlineData("Anne-Marie O'Neil")]
    [InlineData("Armen Mkrtchyan")]
    [InlineData("José Müller")]
    [InlineData("Андрій Шевченко")]
    [InlineData("Нұрлан Әбдіров")]
    [InlineData("Nguyen Van Huy")]
    [InlineData("Dmytro Pidruchnyi")]
    [InlineData("Сергей Бляхин")]
    [InlineData("Ivan Shitov")]
    public void RealNamesPass(string name) => Assert.Null(SignupGuard.CheckName(name));

    [Theory]
    [InlineData("123456789 123456")]
    [InlineData("Ivan")]
    [InlineData("Ivan Petrov2")]
    [InlineData("Ivаn Petrov")] // a Cyrillic "а" among Latin letters
    [InlineData("Ааааа Бббб")]
    [InlineData("Sdfgh Jklm")]
    [InlineData("Qwerty Ivanov")]
    [InlineData("Фыва Пролд")]
    [InlineData("Test Test")]
    [InlineData("Ivan Petrov!")]
    [InlineData("- Petrov")]
    public void JunkNamesFail(string name) => Assert.Equal(SignupGuard.RealName, SignupGuard.CheckName(name));

    [Theory]
    [InlineData("Pidor Ebanov")]
    [InlineData("Хуй Петров")]
    [InlineData("Ivan Admin")]
    [InlineData("Модератор Сети")]
    public void RudeNamesAndStaffTitlesFail(string name) => Assert.Equal(SignupGuard.BadName, SignupGuard.CheckName(name));

    [Theory]
    [InlineData("pilot@mail.ru", null)]
    [InlineData("ivan.petrov@gmail.com", null)]
    [InlineData("12345678@qq.com", null)]
    [InlineData("123@123", SignupGuard.BadEmail)]
    [InlineData("Pidor@ebanov", SignupGuard.BadEmail)]
    [InlineData("a@b.c", SignupGuard.BadEmail)]
    [InlineData("Ivan <ivan@mail.ru>", SignupGuard.BadEmail)]
    [InlineData("62bcc4f00c0b@yourdomain.com", SignupGuard.BadEmail)]
    [InlineData("x@mailinator.com", SignupGuard.TemporaryEmail)]
    [InlineData("x@inbox.yopmail.com", SignupGuard.TemporaryEmail)]
    public void EmailAddresses(string email, string? error) => Assert.Equal(error, SignupGuard.CheckEmail(email));

    [Fact]
    public void NamesAreTidied() => Assert.Equal("Ivan Petrov-Vodkin", SignupGuard.TidyName("  ivan   PETROV-VODKIN "));

    [Fact]
    public async Task JunkRegistrationIsRefused_AndAPersonGetsThrough()
    {
        using var site = new SiteFactory();
        var c = site.Browser();
        foreach (var (name, email, error) in new[]
                 {
                     ("123456789 123456", "pilot@mail.ru", SignupGuard.RealName),
                     ("Pidor Ebanov", "pilot@mail.ru", SignupGuard.BadName),
                     ("Ivan Petrov", "Pidor@ebanov", SignupGuard.BadEmail),
                     ("Ivan Petrov", "x@mailinator.com", SignupGuard.TemporaryEmail),
                 })
            Assert.Contains(error, await (await c.SubmitAsync("/register", Form(name, email))).Content.ReadAsStringAsync());
        // The field people never see.
        var bot = await c.SubmitAsync("/register", new Dictionary<string, string>(Form("Ivan Petrov", "ivan@mail.ru")) { ["Website"] = "http://spam.example" });
        Assert.Contains(SignupGuard.NotAPerson, await bot.Content.ReadAsStringAsync());
        Assert.Equal(0, site.Get<MemberService>().Count());

        var person = await c.SubmitAsync("/register", Form("ivan  PETROV", "ivan@mail.ru"));
        Assert.Equal(HttpStatusCode.Redirect, person.StatusCode);
        Assert.Equal("Ivan Petrov", site.Get<MemberService>().Find(1)!.Name);
    }

    [Fact]
    public async Task FormSentAtOnce_OrWithoutItsTime_IsRefused()
    {
        // Half a minute: sending at once stays "at once" even on a busy test machine.
        using var site = new SiteFactory(new() { ["Site:SignupMinSeconds"] = "30" });
        Assert.Contains(SignupGuard.NotAPerson, await (await RegisterAsync(site.Browser(), Form("Ivan Petrov", "ivan@mail.ru"))).Content.ReadAsStringAsync());
        var forged = await site.Browser().SubmitAsync("/register", new Dictionary<string, string>(Form("Ivan Petrov", "ivan@mail.ru")) { ["Started"] = "0" });
        Assert.Contains(SignupGuard.NotAPerson, await forged.Content.ReadAsStringAsync());
        Assert.Equal(0, site.Get<MemberService>().Count());
    }

    [Fact]
    public async Task FormFilledInForAWhile_IsAccepted()
    {
        using var site = new SiteFactory(new() { ["Site:SignupMinSeconds"] = "1" });
        Assert.Equal(HttpStatusCode.Redirect, (await RegisterAsync(site.Browser(), Form("Ivan Petrov", "ivan@mail.ru"), pauseMs: 1300)).StatusCode);
    }

    [Fact]
    public async Task OnlyAFewAccountsPerAddressAndDay()
    {
        using var site = new SiteFactory(new() { ["Site:RegistrationsPerDayPerAddress"] = "2" });
        Assert.Equal(HttpStatusCode.Redirect, (await site.Browser().SubmitAsync("/register", Form("Ivan Petrov", "ivan@mail.ru"))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await site.Browser().SubmitAsync("/register", Form("Anna Smirnova", "anna@mail.ru"))).StatusCode);
        var third = await site.Browser().SubmitAsync("/register", Form("Oleg Sokolov", "oleg@mail.ru"));
        Assert.Contains("Too many accounts were registered from your address today", await third.Content.ReadAsStringAsync());
        Assert.Equal(2, site.Get<MemberService>().Count());
    }

    [Fact]
    public async Task StaffSeeSuspiciousAccounts_AndSuspendThemTogether()
    {
        using var site = new SiteFactory();
        long sup = site.Member("Sergey Supervisor", Ratings.SUP);
        long junk1 = site.Member("123456 123456"), junk2 = site.Member("Pidor Ebanov"), real = site.Member("Ivan Petrov");
        var s = site.Browser();
        await s.LoginAsync(sup);

        string list = await s.HtmlAsync("/staff/members?show=suspicious");
        Assert.Contains($"/staff/members/{junk1}\"", list);
        Assert.Contains($"/staff/members/{junk2}\"", list);
        Assert.DoesNotContain($"/staff/members/{real}\"", list);

        string html = await s.HtmlAsync("/staff/members?suspicious=1"); // the older link still works
        Assert.Contains("class=\"on\">Suspicious</a>", html);
        string token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value);
        var form = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", token), new("reason", "Junk registration"), new("suspicious", "1"),
            new("cids", junk1.ToString()), new("cids", junk2.ToString()), new("cids", sup.ToString()),
        };
        var r = await s.PostAsync("/staff/members?handler=Suspend", new FormUrlEncodedContent(form));
        Assert.Contains("Suspended: 2. Skipped: 1", await r.Content.ReadAsStringAsync());
        var members = site.Get<MemberService>();
        Assert.True(members.IsSuspended(junk1));
        Assert.True(members.IsSuspended(junk2));
        Assert.False(members.IsSuspended(real));
        Assert.False(members.IsSuspended(sup));
    }
}
