using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using SkyNetwork.Site.Data;

namespace SkyNetwork.Site.Pages;

/// <summary>The link from the password reset letter: sets a new password.</summary>
[EnableRateLimiting("auth")]
public sealed class ResetPasswordModel(EmailTokenService tokens, MemberService members, AuditService audit) : PageModel
{
    [BindProperty(SupportsGet = true)] public string Token { get; set; } = "";
    [BindProperty] public string NewPassword { get; set; } = "";
    [BindProperty] public string Confirm { get; set; } = "";
    public bool Valid { get; private set; }
    public bool Done { get; private set; }
    public long Cid { get; private set; }
    public string? Error { get; private set; }

    public void OnGet()
    {
        if (tokens.Check(Token, EmailTokenService.Reset, consume: false) is (long cid, _)) (Valid, Cid) = (true, cid);
    }

    public IActionResult OnPost()
    {
        OnGet();
        if (!Valid) return Page();
        NewPassword ??= "";
        if (NewPassword.Length < 8) Error = "The new password must be at least 8 characters";
        else if (NewPassword.Contains(':')) Error = "The password cannot contain a colon";
        else if (NewPassword != Confirm) Error = "The passwords do not match";
        else if (tokens.Check(Token, EmailTokenService.Reset, consume: true) is (long cid, _))
        {
            members.ChangePassword(cid, NewPassword);
            audit.Log(cid, "password-reset", cid.ToString(), "by email link");
            Done = true;
        }
        else Valid = false;
        return Page();
    }
}
