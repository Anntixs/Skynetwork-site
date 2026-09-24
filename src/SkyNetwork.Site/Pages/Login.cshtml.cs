using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using SkyNetwork.Site.Data;
using SkyNetwork.Site.Localization;
using SkyNetwork.Site.Security;

namespace SkyNetwork.Site.Pages;

[EnableRateLimiting("auth")]
public sealed class LoginModel(MemberService members, ConnectService connect) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    [BindProperty] public long Cid { get; set; }
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public bool Remember { get; set; }
    public string? Error { get; private set; }

    /// <summary>The site being signed in to through SkyNetwork Connect, if any.</summary>
    public string? ConnectSite
    {
        get
        {
            if (ReturnUrl == null || !ReturnUrl.StartsWith("/oauth/authorize?", StringComparison.Ordinal)) return null;
            var id = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(ReturnUrl[ReturnUrl.IndexOf('?')..])["client_id"].ToString();
            return connect.Client(id) is { Active: true } c ? c.Name : null;
        }
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        var m = members.Authenticate(Cid, Password);
        if (m == null)
        {
            // Only someone who knows the password learns that the account is suspended, and why.
            if (members.IsSuspended(Cid) && members.PasswordMatches(Cid, Password) && members.Find(Cid) is { } s)
            {
                string reason = s.SuspensionReason.Length > 0 ? s.SuspensionReason : this.T("not given");
                Error = s.SuspensionEnds is { } until
                    ? this.T("Your account is suspended until {0}. Reason: {1}", Format.Utc(until), reason)
                    : this.T("Your account is suspended. Reason: {0}", reason);
            }
            else Error = "Wrong CID or password";
            return Page();
        }
        await HttpContext.SignInMemberAsync(m, Remember);
        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/account");
    }
}
