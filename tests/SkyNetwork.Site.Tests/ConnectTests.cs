using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using SkyNetwork.Site.Data;

namespace SkyNetwork.Site.Tests;

/// <summary>SkyNetwork Connect: signing in on other sites through the network.</summary>
public class ConnectTests
{
    private const string Redirect = "https://skyrus.example/login/callback";

    private static async Task<(string Id, string Secret)> Register(SiteFactory site)
    {
        long admin = site.Member("Anna Admin", Ratings.ADM);
        var a = site.Browser();
        await a.LoginAsync(admin);
        var page = await (await a.SubmitAsync("/staff/connect", new Dictionary<string, string> { ["name"] = "SkyRUS", ["redirects"] = Redirect })).Content.ReadAsStringAsync();
        var keys = Regex.Matches(page, "<div class=\"key-box\"[^>]*>([^<]+)</div>").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(2, keys.Count);
        return (keys[0], keys[1]);
    }

    private static string Authorize(string clientId, string state = "xyz", string scope = "profile", string redirect = Redirect, string challenge = "") =>
        $"/oauth/authorize?response_type=code&client_id={clientId}&redirect_uri={Uri.EscapeDataString(redirect)}&scope={Uri.EscapeDataString(scope)}&state={state}"
        + (challenge.Length > 0 ? $"&code_challenge={challenge}&code_challenge_method=S256" : "");

    private static Dictionary<string, string> Query(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var location = r.Headers.Location!.OriginalString;
        Assert.StartsWith(Redirect + "?", location);
        return QueryHelpers.ParseQuery(location[location.IndexOf('?')..]).ToDictionary(q => q.Key, q => q.Value.ToString());
    }

    private static async Task<HttpResponseMessage> Token(SiteFactory site, string id, string secret, string code, string? verifier = null)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = Redirect, ["client_id"] = id, ["client_secret"] = secret,
        };
        if (verifier != null) form["code_verifier"] = verifier;
        return await site.CreateClient().PostAsync("/oauth/token", new FormUrlEncodedContent(form));
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    private static async Task<HttpResponseMessage> UserInfo(SiteFactory site, string token)
    {
        var c = site.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await c.GetAsync("/oauth/userinfo");
    }

    [Fact]
    public async Task SignInOnAnotherSite()
    {
        using var site = new SiteFactory();
        var (id, secret) = await Register(site);
        long m = site.Member("Petr Student", Ratings.S1);
        var b = site.Browser();

        // Not signed in: to the login page, which names the site, and back.
        var toLogin = await b.GetAsync(Authorize(id));
        Assert.Equal(HttpStatusCode.Redirect, toLogin.StatusCode);
        var loginUrl = toLogin.Headers.Location!.OriginalString;
        Assert.StartsWith("/login?returnUrl=", loginUrl);
        Assert.Contains("to continue to SkyRUS", await b.HtmlAsync(loginUrl));
        var signedIn = await b.SubmitAsync(loginUrl, new Dictionary<string, string>
        {
            ["Cid"] = m.ToString(), ["Password"] = "password1", ["ReturnUrl"] = Uri.UnescapeDataString(loginUrl["/login?returnUrl=".Length..]),
        });
        Assert.StartsWith("/oauth/authorize?", signedIn.Headers.Location!.OriginalString);

        // First time: the member agrees; the site gets a code and its state back.
        var consent = await b.HtmlAsync(Authorize(id));
        Assert.Contains("SkyRUS wants to sign you in", consent);
        Assert.Contains("skyrus.example", consent);
        var q = Query(await b.SubmitAsync(Authorize(id), new Dictionary<string, string>
        {
            ["response_type"] = "code", ["client_id"] = id, ["redirect_uri"] = Redirect, ["scope"] = "profile", ["state"] = "xyz", ["decision"] = "allow",
        }, Authorize(id)));
        Assert.Equal("xyz", q["state"]);

        // The site exchanges the code (once) with its secret and reads the profile.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Token(site, id, "sks_wrong", q["code"])).StatusCode);
        var token = await Json(await Token(site, id, secret, q["code"]));
        Assert.Equal("Bearer", token.GetProperty("token_type").GetString());
        var again = await Token(site, id, secret, q["code"]);
        Assert.Equal("invalid_grant", (await Json(again)).GetProperty("error").GetString());

        var user = await Json(await UserInfo(site, token.GetProperty("access_token").GetString()!));
        Assert.Equal((m, "Petr Student", "S1"), (user.GetProperty("cid").GetInt64(), user.GetProperty("name").GetString(), user.GetProperty("rating").GetString()));
        Assert.Equal(JsonValueKind.Null, user.GetProperty("email").ValueKind); // not asked for

        // Next time no question: straight back with a code. Asking for more (email) asks again.
        Assert.Contains("code", Query(await b.GetAsync(Authorize(id))).Keys);
        Assert.Equal(HttpStatusCode.OK, (await b.GetAsync(Authorize(id, scope: "profile email"))).StatusCode);

        // Withdrawn in the account settings: the token dies and the question comes back.
        await b.SubmitAsync("/account/settings", new Dictionary<string, string> { ["clientId"] = id }, "/account/settings?handler=Revoke");
        Assert.Equal(HttpStatusCode.Unauthorized, (await UserInfo(site, token.GetProperty("access_token").GetString()!)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await b.GetAsync(Authorize(id))).StatusCode);
    }

    [Fact]
    public async Task BadRequests_AndPkce_AndSuspension()
    {
        using var site = new SiteFactory();
        var (id, secret) = await Register(site);
        long m = site.Member("Petr Student");
        var b = site.Browser();
        await b.LoginAsync(m);

        // Unknown site or foreign return address: an error page, never a redirect.
        var foreign = await b.GetAsync(Authorize(id, redirect: "https://evil.example/cb"));
        Assert.Equal(HttpStatusCode.OK, foreign.StatusCode);
        Assert.Contains("does not belong to this site", await foreign.Content.ReadAsStringAsync());
        Assert.Contains("not registered", await (await b.GetAsync(Authorize("nope"))).Content.ReadAsStringAsync());
        Assert.Equal("invalid_scope", Query(await b.GetAsync(Authorize(id, scope: "admin")))["error"]);

        // Refusing sends the member back with access_denied.
        var fields = new Dictionary<string, string> { ["response_type"] = "code", ["client_id"] = id, ["redirect_uri"] = Redirect, ["scope"] = "", ["state"] = "s1", ["decision"] = "deny" };
        Assert.Equal("access_denied", Query(await b.SubmitAsync(Authorize(id), fields, Authorize(id)))["error"]);

        // PKCE: the code only works with the right verifier.
        string verifier = "verifier-" + new string('x', 50);
        string challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        fields["decision"] = "allow";
        fields["code_challenge"] = challenge;
        fields["code_challenge_method"] = "S256";
        var code = Query(await b.SubmitAsync(Authorize(id, challenge: challenge), fields, Authorize(id, challenge: challenge)))["code"];
        Assert.Equal("invalid_grant", (await Json(await Token(site, id, secret, code))).GetProperty("error").GetString());
        var code2 = Query(await b.GetAsync(Authorize(id, challenge: challenge)))["code"];
        var token = (await Json(await Token(site, id, secret, code2, verifier))).GetProperty("access_token").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await UserInfo(site, token)).StatusCode);

        // A suspended member is nobody to other sites either.
        site.Get<MemberService>().SetSuspended(0, m, true, "test");
        Assert.Equal(HttpStatusCode.Unauthorized, (await UserInfo(site, token)).StatusCode);

        // Only administrators register sites.
        long sup = site.Member("Sergey Supervisor", Ratings.SUP);
        var s = site.Browser();
        await s.LoginAsync(sup);
        Assert.Equal(HttpStatusCode.NotFound, (await s.GetAsync("/staff/connect")).StatusCode);
        Assert.Equal("Redirect addresses must use https:// (http:// only for localhost)",
            ConnectService.ValidateRedirects("http://skyrus.example/cb", out _));
        Assert.Null(ConnectService.ValidateRedirects("http://127.0.0.1:8100/login/callback", out _));
    }
}
