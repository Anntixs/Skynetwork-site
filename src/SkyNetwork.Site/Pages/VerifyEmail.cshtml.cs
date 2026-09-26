using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using SkyNetwork.Site.Data;

namespace SkyNetwork.Site.Pages;

/// <summary>The link from the confirmation letter: confirms the address (a new one after an email change).</summary>
[EnableRateLimiting("auth")]
public sealed class VerifyEmailModel(EmailTokenService tokens, MemberService members) : PageModel
{
    public bool Confirmed { get; private set; }
    public string Email { get; private set; } = "";

    public void OnGet(string? token)
    {
        if (tokens.Check(token, EmailTokenService.Verify, consume: true) is not (long cid, string email)) return;
        // The address may have been taken by someone else since the letter went out.
        var owner = members.FindByCidOrEmail(email);
        if (owner != null && owner.Cid != cid) return;
        members.ConfirmEmail(cid, email);
        Confirmed = true;
        Email = email;
    }
}
