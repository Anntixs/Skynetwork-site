using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Services;

namespace SkyNetwork.Site.Tests;

/// <summary>Letters are kept here instead of going to a mail server.</summary>
public sealed class RecordingMailSender : IMailSender
{
    public ConcurrentQueue<Letter> Sent { get; } = new();

    public Task SendAsync(Letter letter, CancellationToken ct)
    {
        Sent.Enqueue(letter);
        return Task.CompletedTask;
    }

    /// <summary>The first letter to <paramref name="to"/> whose subject contains <paramref name="subject"/> (waits for the queue).</summary>
    public async Task<Letter> WaitFor(string to, string subject)
    {
        for (int i = 0; i < 100; i++)
        {
            var letter = Sent.FirstOrDefault(l => l.To == to && l.Subject.Contains(subject));
            if (letter != null) return letter;
            await Task.Delay(50);
        }
        throw new Xunit.Sdk.XunitException($"no letter \"{subject}\" to {to}; sent: {string.Join(", ", Sent.Select(l => l.To + " " + l.Subject))}");
    }
}

/// <summary>The site with mail set up; letters are recorded.</summary>
public sealed class MailSiteFactory : IDisposable
{
    public SiteFactory Site { get; } = new();
    public RecordingMailSender Mail { get; } = new();
    public Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> App { get; }

    public MailSiteFactory()
    {
        App = Site.WithWebHostBuilder(b =>
        {
            b.UseSetting("Mail:Host", "smtp.example.com");
            b.UseSetting("Mail:SiteUrl", "https://sky.example.com/");
            b.ConfigureTestServices(s => s.AddSingleton<IMailSender>(Mail));
        });
    }

    public T Get<T>() where T : notnull => App.Services.GetRequiredService<T>();
    public HttpClient Browser() => App.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });

    public void Dispose()
    {
        App.Dispose();
        Site.Dispose();
    }
}

public class EmailTests
{
    private static string Link(Letter letter, string path) =>
        Regex.Match(letter.Body, "https://sky\\.example\\.com" + Regex.Escape(path) + "\\?token=[A-Za-z0-9_-]+").Value;

    [Fact]
    public async Task RegistrationAsksToConfirmTheEmail()
    {
        using var f = new MailSiteFactory();
        var c = f.Browser();
        var r = await c.SubmitAsync("/register", new Dictionary<string, string>
        {
            ["Name"] = "Ivan Petrov", ["Email"] = "ivan@example.com", ["Country"] = "Россия",
            ["Password"] = "secret123", ["Confirm"] = "secret123",
        });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var members = f.Get<MemberService>();
        Assert.False(members.Find(1)!.EmailVerified);
        Assert.Contains("Confirm your email", await c.HtmlAsync("/account"));

        var letter = await f.Mail.WaitFor("ivan@example.com", "confirm your email");
        Assert.Contains("CID: 1", letter.Body);
        string link = Link(letter, "/verify-email");
        Assert.NotEmpty(link);

        // Asking again at once is refused; the link works once.
        var again = await c.SubmitPageFormAsync("/account", "Resend", new Dictionary<string, string>());
        Assert.Contains("wait two minutes", await again.Content.ReadAsStringAsync());
        var path = link.Replace("https://sky.example.com", "");
        Assert.Contains("is confirmed", await c.HtmlAsync(path));
        Assert.True(members.Find(1)!.EmailVerified);
        Assert.Contains("invalid or has expired", await c.HtmlAsync(path));
        Assert.DoesNotContain("Confirm your email", await c.HtmlAsync("/account"));
    }

    [Fact]
    public async Task ChangedEmailTakesEffectOnceConfirmed()
    {
        using var f = new MailSiteFactory();
        long cid = f.Site.Member("Pilot One");
        var members = f.Get<MemberService>();
        string old = members.Find(cid)!.Email!;
        var c = f.Browser();
        await c.LoginAsync(cid);
        var r = await c.SubmitPageFormAsync("/account/settings", "Profile", new Dictionary<string, string> { ["Email"] = "new@example.com", ["Country"] = "" });
        Assert.Contains("changes once you open it", await r.Content.ReadAsStringAsync());
        Assert.Equal(old, members.Find(cid)!.Email);

        var letter = await f.Mail.WaitFor("new@example.com", "confirm your email");
        await c.HtmlAsync(Link(letter, "/verify-email").Replace("https://sky.example.com", ""));
        Assert.Equal("new@example.com", members.Find(cid)!.Email);
    }

    [Fact]
    public async Task ForgottenPasswordIsResetByEmail()
    {
        using var f = new MailSiteFactory();
        long cid = f.Site.Member("Pilot One");
        string email = f.Get<MemberService>().Find(cid)!.Email!;
        var c = f.Browser();
        // Unknown accounts get the same answer.
        var unknown = await c.SubmitAsync("/forgot-password", new Dictionary<string, string> { ["Login"] = "nobody@example.com" });
        Assert.Contains("If there is an account", await unknown.Content.ReadAsStringAsync());
        await c.SubmitAsync("/forgot-password", new Dictionary<string, string> { ["Login"] = cid.ToString() });

        var letter = await f.Mail.WaitFor(email, "password reset");
        var path = Link(letter, "/reset-password").Replace("https://sky.example.com", "");
        var r = await c.SubmitAsync(path, new Dictionary<string, string> { ["NewPassword"] = "brandnew99", ["Confirm"] = "brandnew99" });
        Assert.Contains("The password is changed", await r.Content.ReadAsStringAsync());
        Assert.NotNull(f.Get<MemberService>().Authenticate(cid, "brandnew99"));
        Assert.Contains("invalid or has expired", await c.HtmlAsync(path));
    }

    [Fact]
    public async Task StaffDecisionsAreSentByEmail()
    {
        using var f = new MailSiteFactory();
        long cid = f.Site.Member("Pilot One");
        var members = f.Get<MemberService>();
        string email = members.Find(cid)!.Email!;

        members.SetSuspended(5, cid, true, "rule 2.1", days: 7);
        var suspended = await f.Mail.WaitFor(email, "account suspended");
        Assert.Contains("rule 2.1", suspended.Body);
        Assert.Contains("UTC", suspended.Body);
        members.SetSuspended(5, cid, false, "");
        await f.Mail.WaitFor(email, "suspension lifted");

        members.SetRating(5, cid, Ratings.S1);
        Assert.Contains("OBS → S1", (await f.Mail.WaitFor(email, "new rating")).Body);
        members.SetStaffRank(5, cid, Ratings.SUP);
        await f.Mail.WaitFor(email, "staff rank");

        var support = f.Get<SupportService>();
        long ticket = support.OpenTicket(null, "guest@example.com", "Forgot CID", "help");
        support.Reply(ticket, 5, staff: true, "Your CID is 7");
        Assert.Contains("Your CID is 7", (await f.Mail.WaitFor("guest@example.com", $"#{ticket}")).Body);
    }

    [Fact]
    public async Task WithoutMailNothingIsAsked()
    {
        using var site = new SiteFactory();
        var c = site.Browser();
        await c.SubmitAsync("/register", new Dictionary<string, string>
        {
            ["Name"] = "Ivan Petrov", ["Email"] = "ivan@example.com", ["Country"] = "Россия",
            ["Password"] = "secret123", ["Confirm"] = "secret123",
        });
        Assert.True(site.Get<MemberService>().Find(1)!.EmailVerified);
        Assert.Contains("Write to support", await c.HtmlAsync("/forgot-password"));
    }
}
